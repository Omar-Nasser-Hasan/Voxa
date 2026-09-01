using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;

namespace Voxa.Services
{
    /// <summary>Loads the active UI string dictionary and exposes it to XAML and view models.</summary>
    public sealed class LocalizationService : INotifyPropertyChanged
    {
        private const string English = "en";
        private const string Arabic = "ar";
        private readonly string _settingsPath;
        private ResourceDictionary? _activeDictionary;
        private string _languageCode = English;
        public static LocalizationService Instance { get; } = new();
        public bool IsArabic => _languageCode == Arabic;
        public FlowDirection FlowDirection => IsArabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        public string this[string key] => Get(key);
        private LocalizationService()
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Voxa");
            Directory.CreateDirectory(folder);
            _settingsPath = Path.Combine(folder, "settings.txt");
        }
        public void Initialize()
        {
            try { if (File.Exists(_settingsPath) && File.ReadAllText(_settingsPath).Trim().Equals(Arabic, StringComparison.OrdinalIgnoreCase)) _languageCode = Arabic; } catch { }
            ApplyDictionary();
        }
        public void ToggleLanguage()
        {
            _languageCode = IsArabic ? English : Arabic;
            try { File.WriteAllText(_settingsPath, _languageCode); } catch { }
            ApplyDictionary();
            OnPropertyChanged(nameof(IsArabic));
            OnPropertyChanged(nameof(FlowDirection));
            OnPropertyChanged("Item[]");
        }
        public string Get(string key) => _activeDictionary?[key] as string ?? key;
        public string Format(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, Get(key), args);
        private void ApplyDictionary()
        {
            var uri = new Uri($"/Voxa;component/Localization/Strings.{_languageCode}.xaml", UriKind.Relative);
            var dictionary = (ResourceDictionary)Application.LoadComponent(uri);
            if (_activeDictionary != null) Application.Current.Resources.MergedDictionaries.Remove(_activeDictionary);
            Application.Current.Resources.MergedDictionaries.Add(dictionary);
            _activeDictionary = dictionary;
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
