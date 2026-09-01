using System;
using System.Windows;
using System.Windows.Threading;
using Voxa.Services;

namespace Voxa
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Catch anything that slips through so the app shows a message
            // instead of silently vanishing - important for non-technical users.
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            // Apply the user's saved Light/Dark preference before any window is created
            // so nothing flashes in the wrong theme on startup.
            ThemeService.ApplyStartupTheme();
            LocalizationService.Instance.Initialize();

            // Don't exit just because the first window (setup) closes - SetupWindow closes
            // itself the moment MainWindow opens, and we don't want that to look like "last
            // window closed" and shut the whole app down. SetupWindow restores the normal
            // shutdown-on-main-window-close behavior once MainWindow is actually showing.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var setupWindow = new SetupWindow();
            setupWindow.Show();
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show(
                LocalizationService.Instance.Format("Dialog.Unexpected.Body", Environment.NewLine, e.Exception.Message),
                LocalizationService.Instance["Dialog.Unexpected.Title"],
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            e.Handled = true;
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                MessageBox.Show(
                    LocalizationService.Instance.Format("Dialog.Fatal.Body", Environment.NewLine, ex.Message),
                    LocalizationService.Instance["Dialog.Fatal.Title"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
