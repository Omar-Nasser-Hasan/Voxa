using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Voxa.Models;

namespace Voxa.Services
{
    /// <summary>
    /// Persists a rolling log of finished batch runs as JSON under
    /// %AppData%\Voxa\history.json, following the same storage pattern as PresetManager.
    /// </summary>
    public class BatchHistoryManager
    {
        private readonly string _historyFilePath;

        // Keeps the file small and the in-app list scannable - older entries are
        // dropped automatically rather than growing the log forever.
        private const int MaxEntries = 100;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        public BatchHistoryManager()
        {
            var appDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Voxa");
            Directory.CreateDirectory(appDataFolder);
            _historyFilePath = Path.Combine(appDataFolder, "history.json");
        }

        /// <summary>Loads history, most recent first. Returns an empty list if none exists yet or the file is unreadable.</summary>
        public List<BatchHistoryEntry> LoadHistory()
        {
            try
            {
                if (!File.Exists(_historyFilePath))
                    return new List<BatchHistoryEntry>();

                var json = File.ReadAllText(_historyFilePath);
                var entries = JsonSerializer.Deserialize<List<BatchHistoryEntry>>(json, JsonOptions);
                return entries ?? new List<BatchHistoryEntry>();
            }
            catch
            {
                // A corrupted or unreadable history file should never crash the app on
                // startup - just start with an empty history instead.
                return new List<BatchHistoryEntry>();
            }
        }

        /// <summary>Adds one entry to the front of the log and persists it, trimming old entries beyond MaxEntries.</summary>
        public void AppendEntry(BatchHistoryEntry entry)
        {
            var entries = LoadHistory();
            entries.Insert(0, entry);
            if (entries.Count > MaxEntries)
                entries = entries.Take(MaxEntries).ToList();
            SaveHistory(entries);
        }

        public void ClearHistory()
        {
            SaveHistory(new List<BatchHistoryEntry>());
        }

        private void SaveHistory(List<BatchHistoryEntry> entries)
        {
            var json = JsonSerializer.Serialize(entries, JsonOptions);

            // Write-then-swap avoids leaving a half-written/corrupt file behind
            // if something interrupts the write (e.g. disk full, power loss).
            var tempPath = _historyFilePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Copy(tempPath, _historyFilePath, overwrite: true);
            File.Delete(tempPath);
        }
    }
}
