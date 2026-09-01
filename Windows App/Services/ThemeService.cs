using System;
using System.IO;
using System.Windows;

namespace Voxa.Services
{
    /// <summary>
    /// Switches the app between Light and Dark by swapping the first merged resource
    /// dictionary in App.xaml (the theme dictionary) for another with the same brush keys.
    /// Every window and control uses DynamicResource for its colors, so the whole app
    /// re-skins instantly with no restart needed. The choice is remembered between runs.
    /// </summary>
    public static class ThemeService
    {
        private const string LightUri = "Theme/LightTheme.xaml";
        private const string DarkUri = "Theme/DarkTheme.xaml";

        private static readonly string SettingsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Voxa", "theme.txt");

        public static bool IsDarkMode { get; private set; }

        public static event Action<bool>? ThemeChanged;

        /// <summary>Applies the saved theme (or Light, on first run). Call once at startup.</summary>
        public static void ApplyStartupTheme()
        {
            IsDarkMode = TryLoadSavedPreference();
            Apply(IsDarkMode, save: false);
        }

        public static void Toggle() => Apply(!IsDarkMode, save: true);

        private static void Apply(bool dark, bool save)
        {
            var dictionaries = Application.Current.Resources.MergedDictionaries;

            var themeDict = new ResourceDictionary
            {
                Source = new Uri(dark ? DarkUri : LightUri, UriKind.Relative)
            };

            // The theme dictionary is always merged first (see App.xaml) - replace just that one.
            if (dictionaries.Count > 0)
                dictionaries[0] = themeDict;
            else
                dictionaries.Add(themeDict);

            IsDarkMode = dark;
            if (save) TrySavePreference(dark);
            ThemeChanged?.Invoke(dark);
        }

        private static bool TryLoadSavedPreference()
        {
            try
            {
                return File.Exists(SettingsFilePath) &&
                       File.ReadAllText(SettingsFilePath).Trim().Equals("dark", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false; // don't let a corrupt/locked settings file block startup
            }
        }

        private static void TrySavePreference(bool dark)
        {
            try
            {
                var folder = Path.GetDirectoryName(SettingsFilePath)!;
                Directory.CreateDirectory(folder);
                File.WriteAllText(SettingsFilePath, dark ? "dark" : "light");
            }
            catch
            {
                // Non-essential - worst case the choice doesn't persist to next launch.
            }
        }
    }
}
