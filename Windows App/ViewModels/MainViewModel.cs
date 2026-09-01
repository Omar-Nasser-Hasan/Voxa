using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Voxa.Commands;
using Voxa.Models;
using Voxa.Services;
using Microsoft.Win32;

namespace Voxa.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private static LocalizationService L => LocalizationService.Instance;
        private readonly FFmpegService _ffmpegService;
        private readonly PresetManager _presetManager;
        private readonly BatchHistoryManager _historyManager;
        private readonly MediaPlayer _previewPlayer = new();
        private readonly DispatcherTimer _playbackTimer;
        private CancellationTokenSource? _cts;

        // How many files FFmpeg is allowed to work on at once. Kept modest (not tied to
        // CPU core count) since audio encoding is usually I/O- and single-thread bound
        // per FFmpeg process, and running too many at once mostly just fights over disk
        // I/O without finishing any faster.
        private const int MaxParallelFiles = 3;

        // A failure gets retried automatically since some failures are transient (a
        // locked file, a brief hiccup launching the process) rather than a real problem
        // with the file itself. Kept small so a genuinely broken file doesn't stall the
        // batch by retrying for a long time before giving up.
        private const int MaxRetriesPerFile = 2;

        // Caps how many quality scans run at once when a whole folder is added, so
        // dropping in hundreds of files doesn't spawn hundreds of ffmpeg processes at once.
        private readonly SemaphoreSlim _qualityScanGate = new(2);

        // Cancels a still-running waveform load if the user picks a different file
        // before the previous one finished decoding.
        private CancellationTokenSource? _waveformCts;
        private TimeSpan? _selectedFileDuration;
        private bool _hasPlaybackStarted;

        // ---- Collections -----------------------------------------------------------

        public ObservableCollection<AudioFileItem> Files { get; } = new();
        public ObservableCollection<Preset> Presets { get; } = new();
        public ObservableCollection<BatchHistoryEntry> BatchHistory { get; } = new();
        public ObservableCollection<string> AvailableFormats { get; } =
            new(AudioFileFilter.SupportedOutputFormats);
        public ObservableCollection<int> CommonSampleRates { get; } =
            new(new[] { 8000, 16000, 22050, 32000, 44100, 48000, 96000 });

        public bool HasNoFiles => Files.Count == 0;
        public string QueueCountText => L.Format("Queue.Count", Files.Count);
        public bool HasNoBatchHistory => BatchHistory.Count == 0;

        // ---- Waveform preview -------------------------------------------------------

        private AudioFileItem? _selectedFile;
        public AudioFileItem? SelectedFile
        {
            get => _selectedFile;
            set
            {
                if (SetField(ref _selectedFile, value))
                {
                    StopPreviewPlayback();
                    if (_previewOutput && value?.HasOutputFile != true)
                    {
                        _previewOutput = false;
                        OnPropertyChanged(nameof(PreviewOutput));
                    }
                    OnPropertyChanged(nameof(HasSelectedFile));
                    OnPropertyChanged(nameof(CanPreviewOutput));
                    OnPropertyChanged(nameof(PreviewSourceLabel));
                    OnPropertyChanged(nameof(SelectedPreviewDisplayName));
                    CommandManager.InvalidateRequerySuggested();
                    _ = LoadWaveformAsync(value);
                }
            }
        }

        private float[] _waveformPeaks = Array.Empty<float>();
        public float[] WaveformPeaks
        {
            get => _waveformPeaks;
            private set => SetField(ref _waveformPeaks, value);
        }

        private bool _isWaveformLoading;
        public bool IsWaveformLoading
        {
            get => _isWaveformLoading;
            set => SetField(ref _isWaveformLoading, value);
        }

        public bool HasSelectedFile => SelectedFile != null;

        private bool _previewOutput;
        public bool PreviewOutput
        {
            get => _previewOutput;
            set
            {
                var next = value && CanPreviewOutput;
                if (SetField(ref _previewOutput, next))
                {
                    StopPreviewPlayback();
                    OnPropertyChanged(nameof(PreviewSourceLabel));
                    OnPropertyChanged(nameof(SelectedPreviewDisplayName));
                    CommandManager.InvalidateRequerySuggested();
                    _ = LoadWaveformAsync(SelectedFile);
                }
            }
        }

        public bool CanPreviewOutput => SelectedFile?.HasOutputFile == true;

        public string PreviewSourceLabel => PreviewOutput && CanPreviewOutput
            ? LocalizationService.Instance["Preview.OutputSource"]
            : LocalizationService.Instance["Preview.Input"];

        public string SelectedPreviewDisplayName
        {
            get
            {
                if (SelectedFile == null) return string.Empty;
                return PreviewOutput && CanPreviewOutput
                    ? SelectedFile.OutputFileName
                    : SelectedFile.FileName;
            }
        }

        private bool _isPlaybackPlaying;
        public bool IsPlaybackPlaying
        {
            get => _isPlaybackPlaying;
            set
            {
                if (SetField(ref _isPlaybackPlaying, value))
                    OnPropertyChanged(nameof(PlayPauseButtonText));
            }
        }

        private double _playbackProgress;
        public double PlaybackProgress
        {
            get => _playbackProgress;
            set => SetField(ref _playbackProgress, Math.Max(0, Math.Min(1, value)));
        }

        private string _playbackTimeText = "0:00";
        public string PlaybackTimeText
        {
            get => _playbackTimeText;
            set => SetField(ref _playbackTimeText, value);
        }

        public string PlayPauseButtonText => IsPlaybackPlaying ? LocalizationService.Instance["Preview.Pause"] : LocalizationService.Instance["Preview.Play"];

        // ---- Processing parameters (flattened onto the VM for easy two-way binding) -

        private string _outputFormat = "mp3";
        public string OutputFormat
        {
            get => _outputFormat;
            set
            {
                if (SetField(ref _outputFormat, value))
                    OnPropertyChanged(nameof(FileNamePatternPreview));
                    OnPropertyChanged(nameof(QueueCountText));
            }
        }

        private int _sampleRateHz = 44100;
        public int SampleRateHz
        {
            get => _sampleRateHz;
            set => SetField(ref _sampleRateHz, value);
        }

        private bool _keepOriginalSampleRate;
        public bool KeepOriginalSampleRate
        {
            get => _keepOriginalSampleRate;
            set => SetField(ref _keepOriginalSampleRate, value);
        }

        private double _volumeChangeDb;
        public double VolumeChangeDb
        {
            get => _volumeChangeDb;
            set => SetField(ref _volumeChangeDb, value);
        }

        private bool _enhanceClarity;
        public bool EnhanceClarity
        {
            get => _enhanceClarity;
            set => SetField(ref _enhanceClarity, value);
        }

        private bool _normalizeVolume;
        public bool NormalizeVolume
        {
            get => _normalizeVolume;
            set => SetField(ref _normalizeVolume, value);
        }

        private double _speedMultiplier = 1.0;
        public double SpeedMultiplier
        {
            get => _speedMultiplier;
            set => SetField(ref _speedMultiplier, value);
        }

        private int _bitrateKbps = 192;
        public int BitrateKbps
        {
            get => _bitrateKbps;
            set => SetField(ref _bitrateKbps, value);
        }

        private double _silencePaddingStartSec;
        public double SilencePaddingStartSec
        {
            get => _silencePaddingStartSec;
            set => SetField(ref _silencePaddingStartSec, value);
        }

        private double _silencePaddingEndSec;
        public double SilencePaddingEndSec
        {
            get => _silencePaddingEndSec;
            set => SetField(ref _silencePaddingEndSec, value);
        }

        private bool _trimSilence;
        public bool TrimSilence { get => _trimSilence; set => SetField(ref _trimSilence, value); }

        private bool _writeMetadata;
        public bool WriteMetadata { get => _writeMetadata; set => SetField(ref _writeMetadata, value); }
        private string _metadataTitle = string.Empty;
        public string MetadataTitle { get => _metadataTitle; set => SetField(ref _metadataTitle, value); }
        private string _metadataArtist = string.Empty;
        public string MetadataArtist { get => _metadataArtist; set => SetField(ref _metadataArtist, value); }
        private string _metadataAlbum = string.Empty;
        public string MetadataAlbum { get => _metadataAlbum; set => SetField(ref _metadataAlbum, value); }

        private string _fileNamePattern = "{name}";
        public string FileNamePattern
        {
            get => _fileNamePattern;
            set
            {
                if (SetField(ref _fileNamePattern, value))
                    OnPropertyChanged(nameof(FileNamePatternPreview));
            }
        }

        private bool _useCustomFileNames = true;
        public bool UseCustomFileNames
        {
            get => _useCustomFileNames;
            set
            {
                if (SetField(ref _useCustomFileNames, value))
                    OnPropertyChanged(nameof(FileNamePatternPreview));
            }
        }

        private int _sequenceStart = 1;
        public int SequenceStart
        {
            get => _sequenceStart;
            set
            {
                if (SetField(ref _sequenceStart, value))
                    OnPropertyChanged(nameof(FileNamePatternPreview));
            }
        }

        /// <summary>Read-only example shown under the naming pattern field, e.g. "my_recording -> Interview_003".</summary>
        public string FileNamePatternPreview =>
            UseCustomFileNames
                ? L.Format("Names.Preview", OutputNamer.PreviewExample(FileNamePattern, SequenceStart), OutputFormat)
                : L.Format("Names.OriginalPreview", OutputFormat);

        // ---- Presets -----------------------------------------------------------------

        private Preset? _selectedPreset;
        public Preset? SelectedPreset
        {
            get => _selectedPreset;
            set
            {
                if (SetField(ref _selectedPreset, value) && value != null)
                    ApplyPreset(value);
            }
        }

        private string _newPresetName = string.Empty;
        public string NewPresetName
        {
            get => _newPresetName;
            set => SetField(ref _newPresetName, value);
        }

        // ---- Output / progress state ---------------------------------------------

        private string _outputFolder = string.Empty;
        public string OutputFolder
        {
            get => _outputFolder;
            set => SetField(ref _outputFolder, value);
        }

        private bool _isProcessing;
        public bool IsProcessing
        {
            get => _isProcessing;
            set
            {
                if (SetField(ref _isProcessing, value))
                    CommandManager.InvalidateRequerySuggested();
            }
        }

        private double _overallProgress;
        public double OverallProgress
        {
            get => _overallProgress;
            set => SetField(ref _overallProgress, value);
        }

        private string _statusText = string.Empty;
        public string StatusText
        {
            get => _statusText;
            set => SetField(ref _statusText, value);
        }

        private string _etaText = string.Empty;
        public string EtaText
        {
            get => _etaText;
            set => SetField(ref _etaText, value);
        }

        // ---- Batch-complete banner -------------------------------------------------
        // Replaces the old blocking "finished" MessageBox with a dismissible in-window
        // banner, so the app never interrupts the person with a modal dialog they have
        // to click through.

        private bool _showCompletionBanner;
        public bool ShowCompletionBanner
        {
            get => _showCompletionBanner;
            set => SetField(ref _showCompletionBanner, value);
        }

        private string _completionSummary = string.Empty;
        public string CompletionSummary
        {
            get => _completionSummary;
            set => SetField(ref _completionSummary, value);
        }

        private bool _completionHasFailures;
        public bool CompletionHasFailures
        {
            get => _completionHasFailures;
            set => SetField(ref _completionHasFailures, value);
        }

        // ---- App version / update banner --------------------------------------------
        // A quiet, dismissible strip - never a popup - that only appears if a background
        // GitHub check (see UpdateChecker) finds a newer release. Silent no-op otherwise.

        public string AppVersionText => $"Voxa {Services.AppVersion.Current}";

        private bool _showUpdateBanner;
        public bool ShowUpdateBanner
        {
            get => _showUpdateBanner;
            set => SetField(ref _showUpdateBanner, value);
        }

        private string _updateBannerText = string.Empty;
        public string UpdateBannerText
        {
            get => _updateBannerText;
            set => SetField(ref _updateBannerText, value);
        }

        private string? _updateReleaseUrl;

        // ---- Commands ------------------------------------------------------------

        public ICommand AddFilesCommand { get; }
        public ICommand AddFolderCommand { get; }
        public ICommand ClearFilesCommand { get; }
        public ICommand BrowseOutputFolderCommand { get; }
        public ICommand StartProcessingCommand { get; }
        public ICommand CancelProcessingCommand { get; }
        public ICommand SavePresetCommand { get; }
        public ICommand DeletePresetCommand { get; }
        public ICommand DismissCompletionCommand { get; }
        public ICommand ClearHistoryCommand { get; }
        public ICommand OpenUpdatePageCommand { get; }
        public ICommand DismissUpdateBannerCommand { get; }
        public ICommand TogglePlaybackCommand { get; }

        public MainViewModel()
            : this(new FFmpegService(), new PresetManager(), new BatchHistoryManager())
        {
        }

        // Constructor overload with injectable services - keeps the class testable.
        public MainViewModel(FFmpegService ffmpegService, PresetManager presetManager, BatchHistoryManager historyManager)
        {
            _ffmpegService = ffmpegService;
            _presetManager = presetManager;
            _historyManager = historyManager;
            StatusText = L["Runtime.NoFiles"];

            _playbackTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(33)
            };
            _playbackTimer.Tick += (_, _) => RefreshPlaybackPosition();
            _previewPlayer.MediaEnded += (_, _) => FinishPreviewPlayback();

            foreach (var preset in PresetCatalog.CreateBuiltInPresets())
                Presets.Add(preset);

            foreach (var preset in _presetManager.LoadPresets())
                Presets.Add(preset);

            LocalizationService.Instance.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == "Item[]")
                {
                    OnPropertyChanged(nameof(FileNamePatternPreview));
                    OnPropertyChanged(nameof(PreviewSourceLabel));
                    OnPropertyChanged(nameof(PlayPauseButtonText));
                    foreach (var file in Files) file.RefreshLocalizedStatus();
                    foreach (var preset in Presets) preset.RefreshDisplayName();
                }
            };

            foreach (var entry in _historyManager.LoadHistory())
                BatchHistory.Add(entry);

            Files.CollectionChanged += (_, __) =>
            {
                OnPropertyChanged(nameof(HasNoFiles));
                OnPropertyChanged(nameof(QueueCountText));
            };
            BatchHistory.CollectionChanged += (_, __) => OnPropertyChanged(nameof(HasNoBatchHistory));

            AddFilesCommand = new RelayCommand(_ => AddFilesViaDialog());
            AddFolderCommand = new RelayCommand(_ => AddFolderViaDialog());
            ClearFilesCommand = new RelayCommand(_ => { Files.Clear(); SelectedFile = null; }, _ => Files.Count > 0 && !IsProcessing);
            BrowseOutputFolderCommand = new RelayCommand(_ => BrowseOutputFolder());
            StartProcessingCommand = new AsyncRelayCommand(_ => StartProcessingAsync(), _ => !IsProcessing);
            CancelProcessingCommand = new RelayCommand(_ => CancelProcessing(), _ => IsProcessing);
            SavePresetCommand = new RelayCommand(_ => SavePreset());
            DeletePresetCommand = new RelayCommand(_ => DeletePreset(), _ => SelectedPreset != null);
            DismissCompletionCommand = new RelayCommand(_ => ShowCompletionBanner = false);
            ClearHistoryCommand = new RelayCommand(_ => ClearHistory(), _ => BatchHistory.Count > 0);
            OpenUpdatePageCommand = new RelayCommand(_ => OpenUpdatePage());
            DismissUpdateBannerCommand = new RelayCommand(_ => ShowUpdateBanner = false);
            TogglePlaybackCommand = new RelayCommand(_ => TogglePreviewPlayback(), _ => SelectedFile != null);

            if (!_ffmpegService.IsAvailable)
            {
                // Shouldn't normally happen - SetupWindow already confirmed FFmpeg was
                // ready before this window ever opened. Covers the rare case of something
                // removing the cached copy mid-session (e.g. antivirus quarantine).
                StatusText = "Warning: FFmpeg is missing. Restart the app to set it up again.";
            }

            // Fire-and-forget: never awaited, never blocks the UI from opening, and
            // UpdateChecker itself swallows every failure. Worst case, nothing happens.
            _ = CheckForUpdatesAsync();
        }

        // ---- File queue management -------------------------------------------------

        private void AddFilesViaDialog()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select audio files",
                Multiselect = true,
                Filter = AudioFileFilter.OpenFileDialogFilter()
            };
            if (dialog.ShowDialog() == true)
                AddFiles(dialog.FileNames);
        }

        private void AddFolderViaDialog()
        {
            var dialog = new OpenFolderDialog { Title = "Select a folder of audio files" };
            if (dialog.ShowDialog() == true)
                AddFiles(new[] { dialog.FolderName });
        }

        /// <summary>Adds files and/or whole folders (recursively) to the batch queue, skipping duplicates and unsupported types.</summary>
        public void AddFiles(IEnumerable<string> paths)
        {
            int added = 0, skipped = 0;

            foreach (var path in paths)
            {
                if (Directory.Exists(path))
                {
                    foreach (var file in EnumerateAudioFiles(path))
                        AddSingleFile(file, ref added, ref skipped);
                }
                else if (File.Exists(path))
                {
                    AddSingleFile(path, ref added, ref skipped);
                }
            }

            if (added > 0 && skipped > 0)
                StatusText = L.Format("Runtime.AddedSkipped", added, skipped);
            else if (added > 0)
                StatusText = L.Format("Runtime.Added", added);
            else if (skipped > 0)
                StatusText = L["Runtime.Unsupported"];
        }

        // Number of bars drawn in the waveform preview - enough detail to look like a
        // real waveform without needing a wide window or heavy rendering.
        private const int WaveformBucketCount = 120;

        private async Task LoadWaveformAsync(AudioFileItem? file)
        {
            _waveformCts?.Cancel();
            WaveformPeaks = Array.Empty<float>();
            _selectedFileDuration = null;
            _hasPlaybackStarted = false;
            PlaybackProgress = 0;
            PlaybackTimeText = "0:00";

            var previewPath = GetPreviewPath(file);
            if (file == null || string.IsNullOrWhiteSpace(previewPath) || !File.Exists(previewPath))
            {
                IsWaveformLoading = false;
                return;
            }

            var cts = new CancellationTokenSource();
            _waveformCts = cts;

            IsWaveformLoading = true;
            try
            {
                var duration = await _ffmpegService.GetDurationAsync(previewPath, cts.Token)
                    .ConfigureAwait(true);
                var peaks = await _ffmpegService
                    .GetWaveformPeaksAsync(previewPath, WaveformBucketCount, cts.Token, duration)
                    .ConfigureAwait(true);

                if (cts.IsCancellationRequested || SelectedFile != file) return;

                WaveformPeaks = peaks;
                _selectedFileDuration = duration;
                PlaybackTimeText = FormatPlaybackTime(duration ?? TimeSpan.Zero);
            }
            catch (OperationCanceledException)
            {
                // Expected when the selection changed mid-load - nothing to show for it.
            }
            catch
            {
                // Best-effort preview only - a failed decode just leaves the waveform empty.
            }
            finally
            {
                if (SelectedFile == file) IsWaveformLoading = false;
            }
        }

        private void AddSingleFile(string path, ref int added, ref int skipped)
        {
            if (!AudioFileFilter.IsSupported(path)) { skipped++; return; }
            if (Files.Any(f => string.Equals(f.FilePath, path, StringComparison.OrdinalIgnoreCase))) return;
            var item = new AudioFileItem(path);
            Files.Add(item);
            added++;
            _ = ScanQualityInBackgroundAsync(item);
        }

        /// <summary>
        /// Runs a lightweight loudness scan for one file without blocking the UI or the
        /// caller. Best-effort: any failure just leaves the file unflagged rather than
        /// surfacing an error, since this is a convenience heads-up, not a required step.
        /// </summary>
        private async Task ScanQualityInBackgroundAsync(AudioFileItem item)
        {
            await _qualityScanGate.WaitAsync().ConfigureAwait(false);
            try
            {
                var report = await _ffmpegService.AnalyzeQualityAsync(item.FilePath, CancellationToken.None)
                    .ConfigureAwait(true); // back to the UI thread before touching the item
                if (report.Success && report.HasWarning)
                {
                    item.HasQualityWarning = true;
                    item.QualityWarningMessage = report.WarningMessage;
                }
            }
            catch
            {
                // Non-essential - the file just won't show a warning badge.
            }
            finally
            {
                _qualityScanGate.Release();
            }
        }

        private static IEnumerable<string> EnumerateAudioFiles(string folder)
        {
            IEnumerable<string> all;
            try
            {
                all = Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories);
            }
            catch
            {
                yield break;
            }

            foreach (var file in all)
            {
                if (AudioFileFilter.IsSupported(file))
                    yield return file;
            }
        }

        /// <summary>Removes the given items from the queue. No-ops while a batch is running.</summary>
        public void RemoveFiles(IEnumerable<AudioFileItem> items)
        {
            if (IsProcessing) return;
            var removedList = items.ToList();
            foreach (var item in removedList)
                Files.Remove(item);

            if (SelectedFile != null && removedList.Contains(SelectedFile))
                SelectedFile = null;
        }

        private void BrowseOutputFolder()
        {
            var dialog = new OpenFolderDialog { Title = "Choose where processed files will be saved" };
            if (dialog.ShowDialog() == true)
                OutputFolder = dialog.FolderName;
        }

        // ---- Presets ---------------------------------------------------------------

        private ProcessingParameters BuildParameters() => new()
        {
            OutputFormat = OutputFormat,
            SampleRateHz = SampleRateHz,
            KeepOriginalSampleRate = KeepOriginalSampleRate,
            VolumeChangeDb = VolumeChangeDb,
            EnhanceClarity = EnhanceClarity,
            NormalizeVolume = NormalizeVolume,
            SpeedMultiplier = SpeedMultiplier,
            BitrateKbps = BitrateKbps,
            SilencePaddingStartSec = SilencePaddingStartSec,
            SilencePaddingEndSec = SilencePaddingEndSec,
            TrimSilence = TrimSilence,
            WriteMetadata = WriteMetadata,
            MetadataTitle = MetadataTitle,
            MetadataArtist = MetadataArtist,
            MetadataAlbum = MetadataAlbum,
            FileNamePattern = FileNamePattern,
            UseCustomFileNames = UseCustomFileNames,
            SequenceStart = SequenceStart
        };

        private void ApplyPreset(Preset preset)
        {
            var p = preset.Parameters;
            OutputFormat = p.OutputFormat;
            SampleRateHz = p.SampleRateHz;
            KeepOriginalSampleRate = p.KeepOriginalSampleRate;
            VolumeChangeDb = p.VolumeChangeDb;
            EnhanceClarity = p.EnhanceClarity;
            NormalizeVolume = p.NormalizeVolume;
            SpeedMultiplier = p.SpeedMultiplier;
            BitrateKbps = p.BitrateKbps;
            SilencePaddingStartSec = p.SilencePaddingStartSec;
            SilencePaddingEndSec = p.SilencePaddingEndSec;
            TrimSilence = p.TrimSilence;
            WriteMetadata = p.WriteMetadata;
            MetadataTitle = p.MetadataTitle;
            MetadataArtist = p.MetadataArtist;
            MetadataAlbum = p.MetadataAlbum;
            UseCustomFileNames = p.UseCustomFileNames;
            FileNamePattern = string.IsNullOrWhiteSpace(p.FileNamePattern) ? "{name}" : p.FileNamePattern;
            SequenceStart = p.SequenceStart;
        }

        private void SavePreset()
        {
            var name = NewPresetName?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Give your preset a name first.", "Preset name needed",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var parameters = BuildParameters();
            var errors = ParameterValidator.Validate(parameters);
            if (errors.Count > 0)
            {
                MessageBox.Show(string.Join("\n", errors), "Check your settings before saving",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var existing = Presets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                var overwrite = MessageBox.Show(
                    $"A preset named '{name}' already exists. Overwrite it?",
                    "Overwrite preset", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (overwrite != MessageBoxResult.Yes) return;
                Presets.Remove(existing);
            }

            var preset = new Preset { Name = name, Parameters = parameters };
            Presets.Add(preset);
            _presetManager.SavePresets(Presets.ToList());
            SelectedPreset = preset;
            NewPresetName = string.Empty;
            StatusText = $"Preset '{name}' saved.";
        }

        private void DeletePreset()
        {
            if (SelectedPreset == null) return;

            var confirm = MessageBox.Show(
                $"Delete preset '{SelectedPreset.Name}'? This can't be undone.",
                "Delete preset", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            var name = SelectedPreset.Name;
            Presets.Remove(SelectedPreset);
            _presetManager.SavePresets(Presets.ToList());
            SelectedPreset = null;
            StatusText = $"Preset '{name}' deleted.";
        }

        // ---- Batch history -----------------------------------------------------------

        private void ClearHistory()
        {
            var confirm = MessageBox.Show(
                "Clear the batch history log? This can't be undone.",
                "Clear history", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            BatchHistory.Clear();
            _historyManager.ClearHistory();
        }

        // ---- Update check --------------------------------------------------------------

        private async Task CheckForUpdatesAsync()
        {
            var result = await UpdateChecker.CheckForUpdateAsync().ConfigureAwait(false);
            if (!result.UpdateAvailable) return;

            // Hop back to the UI thread to touch bound properties.
            Application.Current?.Dispatcher.Invoke(() =>
            {
                _updateReleaseUrl = result.ReleaseUrl;
                UpdateBannerText = $"Voxa {result.LatestVersion} is available (you have {Services.AppVersion.Current}).";
                ShowUpdateBanner = true;
            });
        }

        private void OpenUpdatePage()
        {
            if (string.IsNullOrWhiteSpace(_updateReleaseUrl)) return;
            try
            {
                Process.Start(new ProcessStartInfo(_updateReleaseUrl) { UseShellExecute = true });
            }
            catch
            {
                // Non-critical - if the default browser can't be launched for some reason,
                // there's nothing more useful to do than leave the banner as-is.
            }
        }

        // ---- Batch processing --------------------------------------------------------

        private async Task StartProcessingAsync()
        {
            if (Files.Count == 0)
            {
                StatusText = L["Runtime.NoFiles"];
                return;
            }

            if (string.IsNullOrWhiteSpace(OutputFolder))
            {
                StatusText = L["Runtime.NoFolder"];
                MessageBox.Show("Please choose an output folder before starting.", "Output folder needed",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var parameters = BuildParameters();
            var errors = ParameterValidator.Validate(parameters);
            if (errors.Count > 0)
            {
                MessageBox.Show(string.Join("\n", errors), "Check your settings",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!_ffmpegService.IsAvailable)
            {
                MessageBox.Show(
                    "FFmpeg could not be found. Please reinstall the app or check the 'ffmpeg' folder next to the executable.",
                    "Missing FFmpeg", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var sourceFolder = Path.GetDirectoryName(Files.FirstOrDefault()?.FilePath ?? "");
            if (string.Equals(
                    Path.GetFullPath(OutputFolder).TrimEnd(Path.DirectorySeparatorChar),
                    Path.GetFullPath(sourceFolder ?? "").TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(sourceFolder))
            {
                var proceed = MessageBox.Show(
                    "The output folder is the same as your source folder. Processed files will be saved alongside " +
                    "(never overwriting) your originals. Continue?",
                    "Same folder", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (proceed != MessageBoxResult.Yes) return;
            }

            try
            {
                Directory.CreateDirectory(OutputFolder);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not create or access the output folder:\n{ex.Message}",
                    "Output folder error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var spaceCheck = DiskSpaceChecker.Check(Files.Select(f => f.FilePath), OutputFolder);
            if (spaceCheck.CheckSucceeded && spaceCheck.IsLow)
            {
                var proceedAnyway = MessageBox.Show(
                    spaceCheck.FriendlyMessage + "\n\nContinue anyway?",
                    "Low disk space", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (proceedAnyway != MessageBoxResult.Yes) return;
            }

            IsProcessing = true;
            OverallProgress = 0;
            EtaText = string.Empty;
            ShowCompletionBanner = false;
            PreviewOutput = false;
            _cts = new CancellationTokenSource();

            foreach (var file in Files)
            {
                file.Status = ProcessingStatus.Pending;
                file.Progress = 0;
                file.StatusMessage = L["Runtime.Waiting"];
                file.OutputFilePath = null;
            }

            var stopwatch = Stopwatch.StartNew();
            var startedAtUtc = DateTime.UtcNow;
            var total = Files.Count;
            var usedOutputPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pathLock = new object();

            // Sequence numbers must be assigned up front, in queue order, rather than in
            // whatever order parallel tasks happen to finish - otherwise {n} in the naming
            // pattern would come out in a random, non-reproducible order per run.
            var sequenceNumbers = new Dictionary<AudioFileItem, int>();
            var seq = parameters.SequenceStart;
            foreach (var file in Files)
                sequenceNumbers[file] = seq++;

            // Shared, thread-safe counters - multiple files are processed concurrently
            // below, so plain int++ would lose updates under contention.
            var completedCount = 0;
            var failedCount = 0;
            var skippedCount = 0;
            var retriedCount = 0;
            var processedCount = 0;
            var failedFiles = new List<FailedFileEntry>();
            var failedFilesLock = new object();

            void ReportProgressSafe()
            {
                var processed = Interlocked.CompareExchange(ref processedCount, 0, 0);
                ReportOverall(stopwatch.Elapsed, processed, total);
            }

            async Task ProcessOneFileAsync(AudioFileItem file)
            {
                if (_cts!.Token.IsCancellationRequested)
                {
                    file.Status = ProcessingStatus.Skipped;
                    file.StatusMessage = L["Runtime.Cancelled"];
                    Interlocked.Increment(ref skippedCount);
                    Interlocked.Increment(ref processedCount);
                    ReportProgressSafe();
                    return;
                }

                if (!File.Exists(file.FilePath))
                {
                    file.Status = ProcessingStatus.Skipped;
                    file.StatusMessage = L["Runtime.FileMissing"];
                    Interlocked.Increment(ref skippedCount);
                    Interlocked.Increment(ref processedCount);
                    ReportProgressSafe();
                    return;
                }

                file.Status = ProcessingStatus.Processing;
                file.StatusMessage = L["Runtime.Processing"];

                var sequenceNumber = sequenceNumbers[file];
                var desiredBaseName = parameters.UseCustomFileNames
                    ? OutputNamer.BuildFileName(parameters.FileNamePattern, file.FilePath, sequenceNumber)
                    : Path.GetFileNameWithoutExtension(file.FilePath);
                var desiredName = desiredBaseName + "." + parameters.OutputFormat;

                string outputPath;
                lock (pathLock)
                {
                    outputPath = MakeUniqueOutputPath(Path.Combine(OutputFolder, desiredName), usedOutputPaths);
                }

                var fileProgress = new Progress<double>(pct => file.Progress = pct);

                FFmpegResult? result = null;
                var attempt = 0;

                try
                {
                    while (true)
                    {
                        result = await _ffmpegService.ProcessFileAsync(
                            file.FilePath, outputPath, parameters, fileProgress, _cts.Token);

                        if (result.Success || _cts.Token.IsCancellationRequested)
                            break;

                        // Only retry while attempts remain - a file that fails
                        // MaxRetriesPerFile+1 times in a row is treated as a real failure,
                        // not a transient hiccup, so the batch doesn't stall on one file.
                        if (attempt >= MaxRetriesPerFile)
                            break;

                        attempt++;
                        Interlocked.Increment(ref retriedCount);
                        file.RetryCount = attempt;
                        file.StatusMessage = $"Retrying (attempt {attempt + 1} of {MaxRetriesPerFile + 1})...";

                        // Brief pause before retrying - gives a transient issue (a file
                        // briefly locked by another program, a momentary hiccup starting
                        // the FFmpeg process) a moment to clear on its own.
                        await Task.Delay(TimeSpan.FromSeconds(1.5), _cts.Token).ConfigureAwait(true);
                    }

                    if (result.Success)
                    {
                        file.OutputFilePath = outputPath;
                        file.Status = ProcessingStatus.Success;
                        file.StatusMessage = attempt > 0
                            ? $"Saved as {Path.GetFileName(outputPath)} (after {attempt} retr{(attempt == 1 ? "y" : "ies")})"
                            : $"Saved as {Path.GetFileName(outputPath)}";
                        file.Progress = 100;
                        Interlocked.Increment(ref completedCount);
                        Application.Current?.Dispatcher.Invoke(() =>
                        {
                            OnPropertyChanged(nameof(CanPreviewOutput));
                            OnPropertyChanged(nameof(PreviewSourceLabel));
                            OnPropertyChanged(nameof(SelectedPreviewDisplayName));
                            CommandManager.InvalidateRequerySuggested();
                            if (SelectedFile == file && PreviewOutput)
                                _ = LoadWaveformAsync(file);
                        });
                    }
                    else
                    {
                        file.Status = ProcessingStatus.Failed;
                        file.StatusMessage = result.ErrorMessage ?? "Unknown error";
                        Interlocked.Increment(ref failedCount);
                        lock (failedFilesLock)
                        {
                            failedFiles.Add(new FailedFileEntry
                            {
                                FileName = file.FileName,
                                ErrorMessage = file.StatusMessage
                            });
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    file.Status = ProcessingStatus.Skipped;
                    file.StatusMessage = L["Runtime.Cancelled"];
                    Interlocked.Increment(ref skippedCount);
                }
                catch (Exception ex)
                {
                    // Any unexpected failure on one file should never stop the whole batch.
                    file.Status = ProcessingStatus.Failed;
                    file.StatusMessage = $"Unexpected error: {ex.Message}";
                    Interlocked.Increment(ref failedCount);
                    lock (failedFilesLock)
                    {
                        failedFiles.Add(new FailedFileEntry { FileName = file.FileName, ErrorMessage = ex.Message });
                    }
                }

                Interlocked.Increment(ref processedCount);
                ReportProgressSafe();

                var doneSoFar = Interlocked.CompareExchange(ref processedCount, 0, 0);
                StatusText = L.Format("Runtime.Processed", doneSoFar, total);
            }

            using (var gate = new SemaphoreSlim(MaxParallelFiles))
            {
                var tasks = new List<Task>();
                foreach (var file in Files)
                {
                    await gate.WaitAsync(_cts.Token).ConfigureAwait(true);
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            await ProcessOneFileAsync(file).ConfigureAwait(false);
                        }
                        finally
                        {
                            gate.Release();
                        }
                    }));
                }

                try
                {
                    await Task.WhenAll(tasks).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    // Individual tasks already record their own cancelled/skipped state -
                    // this only stops us from throwing out of StartProcessingAsync itself.
                }
            }

            stopwatch.Stop();
            var wasCancelled = _cts.Token.IsCancellationRequested;
            IsProcessing = false;
            EtaText = string.Empty;
            StatusText = L.Format("Runtime.Finished", completedCount, failedCount, skippedCount);

            CompletionHasFailures = failedCount > 0;
            CompletionSummary = failedCount > 0
                ? $"{completedCount} file(s) processed successfully. {failedCount} file(s) failed - check the Details column for each one."
                : skippedCount > 0
                    ? $"{completedCount} file(s) processed successfully. {skippedCount} skipped."
                    : $"All {completedCount} file(s) processed successfully.";
            ShowCompletionBanner = true;

            var historyEntry = new BatchHistoryEntry
            {
                StartedAtUtc = startedAtUtc,
                FinishedAtUtc = DateTime.UtcNow,
                TotalFiles = total,
                SucceededCount = completedCount,
                FailedCount = failedCount,
                SkippedCount = skippedCount,
                RetriedCount = retriedCount,
                OutputFolder = OutputFolder,
                OutputFormat = parameters.OutputFormat,
                WasCancelled = wasCancelled,
                FailedFiles = failedFiles
            };
            BatchHistory.Insert(0, historyEntry);
            _historyManager.AppendEntry(historyEntry);
        }

        private void ReportOverall(TimeSpan elapsed, int processed, int total)
        {
            OverallProgress = total == 0 ? 0 : processed / (double)total * 100.0;

            if (processed == 0)
            {
                EtaText = string.Empty;
                return;
            }

            var avgPerFile = elapsed.TotalSeconds / processed;
            var remainingFiles = total - processed;
            if (remainingFiles <= 0)
            {
                EtaText = string.Empty;
                return;
            }

            var remaining = TimeSpan.FromSeconds(avgPerFile * remainingFiles);
            EtaText = remaining.TotalSeconds < 1 ? L["Runtime.AlmostFinished"] : L.Format("Runtime.Remaining", FormatTimeSpan(remaining));
        }

        private static string FormatTimeSpan(TimeSpan ts)
        {
            if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}h {ts.Minutes}m";
            if (ts.TotalMinutes >= 1) return $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
            return $"{Math.Max(1, ts.Seconds)}s";
        }

        private void TogglePreviewPlayback()
        {
            var previewPath = GetPreviewPath(SelectedFile);
            if (string.IsNullOrWhiteSpace(previewPath) || !File.Exists(previewPath)) return;

            if (IsPlaybackPlaying)
            {
                _previewPlayer.Pause();
                IsPlaybackPlaying = false;
                _playbackTimer.Stop();
                RefreshPlaybackPosition();
                return;
            }

            if (!_hasPlaybackStarted || _previewPlayer.Source == null)
            {
                _previewPlayer.Open(new Uri(previewPath));
                var startProgress = PlaybackProgress >= 0.999 ? 0 : PlaybackProgress;
                _previewPlayer.Position = PlaybackTimeFromProgress(startProgress);
                _hasPlaybackStarted = true;
            }

            _previewPlayer.Play();
            IsPlaybackPlaying = true;
            _playbackTimer.Start();
            RefreshPlaybackPosition();
        }

        public void SeekPreviewToRatio(double ratio, bool commit)
        {
            ratio = Math.Max(0, Math.Min(1, ratio));
            PlaybackProgress = ratio;

            var target = PlaybackTimeFromProgress(ratio);
            PlaybackTimeText = FormatPlaybackTime(target);

            if (commit && _previewPlayer.Source != null)
            {
                _previewPlayer.Position = target;
                _hasPlaybackStarted = true;
            }
        }

        private void StopPreviewPlayback()
        {
            _playbackTimer.Stop();
            _previewPlayer.Stop();
            _previewPlayer.Close();
            IsPlaybackPlaying = false;
            _hasPlaybackStarted = false;
            PlaybackProgress = 0;
        }

        private void FinishPreviewPlayback()
        {
            _playbackTimer.Stop();
            IsPlaybackPlaying = false;
            _hasPlaybackStarted = false;
            if (_selectedFileDuration is { } duration)
            {
                PlaybackProgress = 1;
                PlaybackTimeText = FormatPlaybackTime(duration);
            }
            OnPropertyChanged(nameof(PlayPauseButtonText));
        }

        private void RefreshPlaybackPosition()
        {
            var current = _previewPlayer.Position;
            var duration = _selectedFileDuration;

            if (duration == null && _previewPlayer.NaturalDuration.HasTimeSpan)
            {
                duration = _previewPlayer.NaturalDuration.TimeSpan;
                _selectedFileDuration = duration;
            }

            if (_hasPlaybackStarted)
                PlaybackTimeText = FormatPlaybackTime(current);
            else
                PlaybackTimeText = FormatPlaybackTime(duration ?? TimeSpan.Zero);

            PlaybackProgress = duration is { TotalSeconds: > 0 }
                ? current.TotalSeconds / duration.Value.TotalSeconds
                : 0;
        }

        private TimeSpan PlaybackTimeFromProgress(double progress)
        {
            progress = Math.Max(0, Math.Min(1, progress));
            return _selectedFileDuration is { TotalSeconds: > 0 } duration
                ? TimeSpan.FromSeconds(duration.TotalSeconds * progress)
                : TimeSpan.Zero;
        }

        private string? GetPreviewPath(AudioFileItem? file)
        {
            if (file == null) return null;
            return PreviewOutput && file.HasOutputFile
                ? file.OutputFilePath
                : file.FilePath;
        }

        private static string FormatPlaybackTime(TimeSpan ts)
        {
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}:{ts.Minutes:00}:{ts.Seconds:00}";

            return $"{(int)ts.TotalMinutes}:{ts.Seconds:00}";
        }

        private static string MakeUniqueOutputPath(string desiredPath, HashSet<string> used)
        {
            if (!File.Exists(desiredPath) && used.Add(desiredPath))
                return desiredPath;

            var dir = Path.GetDirectoryName(desiredPath) ?? string.Empty;
            var name = Path.GetFileNameWithoutExtension(desiredPath);
            var ext = Path.GetExtension(desiredPath);

            var i = 1;
            string candidate;
            do
            {
                candidate = Path.Combine(dir, $"{name}_{i}{ext}");
                i++;
            } while (File.Exists(candidate) || !used.Add(candidate));

            return candidate;
        }

        private void CancelProcessing()
        {
            _cts?.Cancel();
            StatusText = L["Runtime.Cancelling"];
        }

        /// <summary>Called from MainWindow's Closing handler if the user quits mid-batch.</summary>
        public void CancelProcessingOnExit() => _cts?.Cancel();
    }
}
