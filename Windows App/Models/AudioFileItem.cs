using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Voxa.Services;

namespace Voxa.Models
{
    /// <summary>
    /// Represents a single audio file queued for (or having gone through) processing.
    /// Implements INotifyPropertyChanged directly so the ListView in MainWindow can
    /// show live status/progress updates per row while a batch runs.
    /// </summary>
    public class AudioFileItem : INotifyPropertyChanged
    {
        private ProcessingStatus _status = ProcessingStatus.Pending;
        private string _statusMessage = "Waiting";
        private double _progress;
        private bool _hasQualityWarning;
        private string _qualityWarningMessage = string.Empty;
        private int _retryCount;
        private string? _outputFilePath;

        public string FilePath { get; }

        public string FileName => Path.GetFileName(FilePath);

        public string? OutputFilePath
        {
            get => _outputFilePath;
            set
            {
                if (_outputFilePath == value) return;
                _outputFilePath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(OutputFileName));
                OnPropertyChanged(nameof(HasOutputFile));
            }
        }

        public string OutputFileName => string.IsNullOrWhiteSpace(OutputFilePath)
            ? string.Empty
            : Path.GetFileName(OutputFilePath);

        public bool HasOutputFile => !string.IsNullOrWhiteSpace(OutputFilePath) && File.Exists(OutputFilePath);

        public ProcessingStatus Status
        {
            get => _status;
            set
            {
                if (_status == value) return;
                _status = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusDisplay));
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                if (_statusMessage == value) return;
                _statusMessage = value;
                OnPropertyChanged();
            }
        }

        /// <summary>0-100 progress for this specific file, driven by FFmpeg's own progress output.</summary>
        public double Progress
        {
            get => _progress;
            set
            {
                if (System.Math.Abs(_progress - value) < 0.01) return;
                _progress = value;
                OnPropertyChanged();
            }
        }

        /// <summary>True once a background quality scan has flagged this file (clipping or very low volume).</summary>
        public bool HasQualityWarning
        {
            get => _hasQualityWarning;
            set
            {
                if (_hasQualityWarning == value) return;
                _hasQualityWarning = value;
                OnPropertyChanged();
            }
        }

        /// <summary>Plain-language description of the quality issue, shown as a tooltip on the warning icon.</summary>
        public string QualityWarningMessage
        {
            get => _qualityWarningMessage;
            set
            {
                if (_qualityWarningMessage == value) return;
                _qualityWarningMessage = value;
                OnPropertyChanged();
            }
        }

        /// <summary>How many times processing this file was automatically retried after a failure.</summary>
        public int RetryCount
        {
            get => _retryCount;
            set
            {
                if (_retryCount == value) return;
                _retryCount = value;
                OnPropertyChanged();
            }
        }

        public string StatusDisplay => Status switch
        {
            ProcessingStatus.Pending => LocalizationService.Instance["Status.Waiting"],
            ProcessingStatus.Processing => LocalizationService.Instance["Status.Processing"],
            ProcessingStatus.Success => LocalizationService.Instance["Status.Done"],
            ProcessingStatus.Failed => LocalizationService.Instance["Status.Failed"],
            ProcessingStatus.Skipped => LocalizationService.Instance["Status.Skipped"],
            _ => LocalizationService.Instance["Status.Unknown"]
        };

        public void RefreshLocalizedStatus() => OnPropertyChanged(nameof(StatusDisplay));

        public AudioFileItem(string filePath)
        {
            FilePath = filePath;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
