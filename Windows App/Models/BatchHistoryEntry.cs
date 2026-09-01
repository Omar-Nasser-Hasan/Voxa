using System;
using System.Collections.Generic;

namespace Voxa.Models
{
    /// <summary>
    /// A record of one finished (or cancelled) batch run, kept so a non-technical user can
    /// look back and answer "did that big export last week actually finish, and how many
    /// files failed?" without having to remember or re-run anything.
    /// </summary>
    public class BatchHistoryEntry
    {
        public DateTime StartedAtUtc { get; set; }
        public DateTime FinishedAtUtc { get; set; }

        public int TotalFiles { get; set; }
        public int SucceededCount { get; set; }
        public int FailedCount { get; set; }
        public int SkippedCount { get; set; }
        public int RetriedCount { get; set; }

        public string OutputFolder { get; set; } = string.Empty;
        public string OutputFormat { get; set; } = string.Empty;

        /// <summary>True if the run was stopped early by the user rather than finishing on its own.</summary>
        public bool WasCancelled { get; set; }

        /// <summary>File names (not full paths, to keep the log compact and less sensitive) that failed, with their error.</summary>
        public List<FailedFileEntry> FailedFiles { get; set; } = new();

        public TimeSpan Duration => FinishedAtUtc - StartedAtUtc;

        public string Summary =>
            $"{SucceededCount} succeeded, {FailedCount} failed, {SkippedCount} skipped" +
            (WasCancelled ? " (cancelled)" : "");
    }

    public class FailedFileEntry
    {
        public string FileName { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
    }
}
