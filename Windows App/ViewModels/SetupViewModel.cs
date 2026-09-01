using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Voxa.Commands;
using Voxa.Services;

namespace Voxa.ViewModels
{
    /// <summary>
    /// Drives the one-time "setting things up" screen shown before the main window opens.
    /// Most launches this finishes instantly (FFmpeg already cached); only the very first
    /// launch on a machine actually downloads anything.
    /// </summary>
    public class SetupViewModel : ViewModelBase
    {
        private readonly FFmpegBootstrapper _bootstrapper = new();
        private CancellationTokenSource? _cts;

        private double _percent;
        public double Percent
        {
            get => _percent;
            set => SetField(ref _percent, value);
        }

        private bool _isIndeterminate = true;
        public bool IsIndeterminate
        {
            get => _isIndeterminate;
            set => SetField(ref _isIndeterminate, value);
        }

        private string _statusMessage = "Getting everything ready...";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetField(ref _statusMessage, value);
        }

        private bool _hasError;
        public bool HasError
        {
            get => _hasError;
            set
            {
                if (SetField(ref _hasError, value))
                    OnPropertyChanged(nameof(IsSettingUp));
            }
        }

        /// <summary>Convenience for the "still working, show the progress bar" panel.</summary>
        public bool IsSettingUp => !HasError;

        private string _errorMessage = string.Empty;
        public string ErrorMessage
        {
            get => _errorMessage;
            set => SetField(ref _errorMessage, value);
        }

        /// <summary>Raised on a background-friendly (UI) thread once FFmpeg is confirmed ready.</summary>
        public event Action? SetupSucceeded;

        /// <summary>Raised if the user cancels while a download is in progress.</summary>
        public event Action? SetupCancelled;

        public ICommand RetryCommand { get; }
        public ICommand QuitCommand { get; }
        public ICommand CancelCommand { get; }

        public SetupViewModel()
        {
            RetryCommand = new RelayCommand(_ => _ = RunAsync());
            QuitCommand = new RelayCommand(_ => System.Windows.Application.Current.Shutdown());
            CancelCommand = new RelayCommand(_ => _cts?.Cancel(), _ => IsSettingUp);
        }

        public async Task RunAsync()
        {
            HasError = false;
            ErrorMessage = string.Empty;
            IsIndeterminate = true;
            Percent = 0;
            _cts = new CancellationTokenSource();

            var progress = new Progress<SetupProgress>(p =>
            {
                IsIndeterminate = p.IsIndeterminate;
                if (!p.IsIndeterminate) Percent = p.Percent;
                if (!string.IsNullOrEmpty(p.Message)) StatusMessage = p.Message;
            });

            try
            {
                await _bootstrapper.EnsureReadyAsync(progress, _cts.Token).ConfigureAwait(true);
                SetupSucceeded?.Invoke();
            }
            catch (OperationCanceledException)
            {
                SetupCancelled?.Invoke();
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = ex.Message;
            }
        }
    }
}
