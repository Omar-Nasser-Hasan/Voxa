using System.Windows;
using Voxa.ViewModels;

namespace Voxa
{
    /// <summary>
    /// First window shown on every launch. Almost always this is invisible-fast (FFmpeg is
    /// already cached from a previous run), but on a brand-new machine it downloads and
    /// caches FFmpeg here - with visible progress - before the real app ever appears, so
    /// the person never has to go find, download, or install anything by hand.
    /// </summary>
    public partial class SetupWindow : Window
    {
        private readonly SetupViewModel _viewModel;

        public SetupWindow()
        {
            InitializeComponent();
            _viewModel = new SetupViewModel();
            DataContext = _viewModel;

            _viewModel.SetupSucceeded += OnSetupSucceeded;
            _viewModel.SetupCancelled += OnSetupCancelled;

            Loaded += async (_, _) => await _viewModel.RunAsync();
        }

        private void OnSetupSucceeded()
        {
            var main = new MainWindow();
            Application.Current.MainWindow = main;

            // From here on, closing the real window should close the app - restore the
            // normal single-window shutdown behavior now that setup is done.
            Application.Current.ShutdownMode = ShutdownMode.OnMainWindowClose;

            main.Show();
            Close();
        }

        private void OnSetupCancelled()
        {
            Application.Current.Shutdown();
        }
    }
}
