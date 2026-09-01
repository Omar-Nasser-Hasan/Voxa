using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Voxa.Models;

namespace Voxa.Services
{
    /// <summary>
    /// Persists user presets as JSON under
    /// %AppData%\Voxa\presets.json so they survive app updates
    /// and reinstalls (as long as the same Windows user profile is used).
    /// </summary>
    public class PresetManager
    {
        private readonly string _presetsFilePath;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        public PresetManager()
        {
            var appDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Voxa");
            Directory.CreateDirectory(appDataFolder);
            _presetsFilePath = Path.Combine(appDataFolder, "presets.json");
        }

        public List<Preset> LoadPresets()
        {
            try
            {
                if (!File.Exists(_presetsFilePath))
                    return new List<Preset>();

                var json = File.ReadAllText(_presetsFilePath);
                var presets = JsonSerializer.Deserialize<List<Preset>>(json, JsonOptions);
                return (presets ?? new List<Preset>()).Where(p => !p.IsBuiltIn).ToList();
            }
            catch
            {
                // A corrupted or unreadable presets file should never crash the app on
                // startup - just start with an empty preset list instead.
                return new List<Preset>();
            }
        }

        public void SavePresets(List<Preset> presets)
        {
            var json = JsonSerializer.Serialize(presets.Where(p => !p.IsBuiltIn).ToList(), JsonOptions);

            // Write-then-swap avoids leaving a half-written/corrupt file behind
            // if something interrupts the write (e.g. disk full, power loss).
            var tempPath = _presetsFilePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Copy(tempPath, _presetsFilePath, overwrite: true);
            File.Delete(tempPath);
        }
    }
}
