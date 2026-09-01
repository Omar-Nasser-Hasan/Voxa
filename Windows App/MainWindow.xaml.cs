using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Voxa.Models;
using Voxa.Services;
using Voxa.ViewModels;

namespace Voxa
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private bool _isDraggingWaveform;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel();
            DataContext = _viewModel;

            UpdateThemeButtonIcon(ThemeService.IsDarkMode);
            ThemeService.ThemeChanged += UpdateThemeButtonIcon;
            Unloaded += (_, _) => ThemeService.ThemeChanged -= UpdateThemeButtonIcon;
        }

        private void ThemeToggle_Click(object sender, RoutedEventArgs e) => ThemeService.Toggle();
        private void LanguageToggle_Click(object sender, RoutedEventArgs e) => LocalizationService.Instance.ToggleLanguage();

        // Sun in light mode (tap to go dark), moon in dark mode (tap to go light).
        private void UpdateThemeButtonIcon(bool isDark) =>
            ThemeToggleButton.Content = isDark ? "\u2600" : "\uD83C\uDF19";

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
                _viewModel.AddFiles(paths);
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void RemoveSelected_Click(object sender, RoutedEventArgs e)
        {
            var items = FilesListView.SelectedItems.Cast<AudioFileItem>().ToList();
            _viewModel.RemoveFiles(items);
        }

        private void OpenOutputFolder_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_viewModel.OutputFolder) || !Directory.Exists(_viewModel.OutputFolder))
            {
                MessageBox.Show(LocalizationService.Instance["Dialog.NoFolder.Body"], LocalizationService.Instance["Dialog.NoFolder.Title"],
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_viewModel.OutputFolder}\"")
            {
                UseShellExecute = true
            });
        }

        private void WaveformViewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDraggingWaveform = true;
            WaveformViewport.CaptureMouse();
            SeekWaveformToMouse(e, commit: false);
        }

        private void WaveformViewport_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingWaveform)
                SeekWaveformToMouse(e, commit: false);
        }

        private void WaveformViewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDraggingWaveform) return;

            SeekWaveformToMouse(e, commit: true);
            _isDraggingWaveform = false;
            WaveformViewport.ReleaseMouseCapture();
        }

        private void SeekWaveformToMouse(MouseEventArgs e, bool commit)
        {
            if (WaveformViewport.ActualWidth <= 0) return;

            var x = e.GetPosition(WaveformViewport).X;
            _viewModel.SeekPreviewToRatio(x / WaveformViewport.ActualWidth, commit);
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (!_viewModel.IsProcessing) return;

            var result = MessageBox.Show(
                LocalizationService.Instance["Dialog.Quit.Body"],
                LocalizationService.Instance["Dialog.Quit.Title"],
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }

            _viewModel.CancelProcessingOnExit();
        }
    }
}
