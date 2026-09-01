using Voxa.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Voxa.Models
{
    /// <summary>A named, reusable ProcessingParameters configuration, e.g. "Podcast format".</summary>
    public class Preset : INotifyPropertyChanged
    {
        public string Name { get; set; } = string.Empty;
        public string? LocalizationKey { get; set; }
        public bool IsBuiltIn { get; set; }
        public ProcessingParameters Parameters { get; set; } = new();

        public string DisplayName => string.IsNullOrWhiteSpace(LocalizationKey) ? Name : LocalizationService.Instance[LocalizationKey];

        public void RefreshDisplayName() => OnPropertyChanged(nameof(DisplayName));

        public override string ToString() => DisplayName;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
