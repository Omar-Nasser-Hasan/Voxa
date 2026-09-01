using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Voxa.Services
{
    public class DiskSpaceCheckResult
    {
        /// <summary>False if the check itself couldn't run (e.g. bad path) - callers should not block processing on a failed check.</summary>
        public bool CheckSucceeded { get; init; }

        public bool IsLow { get; init; }

        public long RequiredBytesEstimate { get; init; }

        public long AvailableBytes { get; init; }

        /// <summary>Plain-language explanation shown to the user when space looks tight.</summary>
        public string FriendlyMessage { get; init; } = string.Empty;
    }

    /// <summary>
    /// Rough, conservative pre-flight check for "will this batch plausibly fit on the
    /// output drive". Not exact - encoded output size depends heavily on format and
    /// settings - but catches the common case (a big batch aimed at an almost-full
    /// drive) before the user waits through a long run only to have files fail to write
    /// near the end.
    /// </summary>
    public static class DiskSpaceChecker
    {
        // Multiplier applied to total input size to get a safety-margin estimate of
        // output size. Covers formats that can end up larger than the source (e.g.
        // converting a compressed format to WAV) plus some headroom.
        private const double SizeSafetyMultiplier = 1.5;

        // Minimum free space to require beyond the estimate, so the user's drive isn't
        // left completely full even in the best case.
        private const long MinimumHeadroomBytes = 200L * 1024 * 1024; // 200 MB

        public static DiskSpaceCheckResult Check(IEnumerable<string> inputPaths, string outputFolder)
        {
            try
            {
                long totalInputBytes = 0;
                foreach (var path in inputPaths)
                {
                    try
                    {
                        if (File.Exists(path))
                            totalInputBytes += new FileInfo(path).Length;
                    }
                    catch
                    {
                        // Skip files we can't stat - doesn't invalidate the overall estimate.
                    }
                }

                var requiredEstimate = (long)(totalInputBytes * SizeSafetyMultiplier) + MinimumHeadroomBytes;

                var root = Path.GetPathRoot(Path.GetFullPath(outputFolder));
                if (string.IsNullOrEmpty(root))
                {
                    return new DiskSpaceCheckResult { CheckSucceeded = false };
                }

                var drive = new DriveInfo(root);
                var availableBytes = drive.AvailableFreeSpace;

                var isLow = availableBytes < requiredEstimate;

                return new DiskSpaceCheckResult
                {
                    CheckSucceeded = true,
                    IsLow = isLow,
                    RequiredBytesEstimate = requiredEstimate,
                    AvailableBytes = availableBytes,
                    FriendlyMessage = isLow
                        ? $"The output drive has about {FormatBytes(availableBytes)} free, but this batch may need " +
                          $"around {FormatBytes(requiredEstimate)}. Processing could run out of space partway through."
                        : string.Empty
                };
            }
            catch
            {
                // Never let a failed disk-space check block processing - it's a
                // best-effort warning, not a hard requirement.
                return new DiskSpaceCheckResult { CheckSucceeded = false };
            }
        }

        private static string FormatBytes(long bytes)
        {
            double value = bytes;
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            var unitIndex = 0;
            while (value >= 1024 && unitIndex < units.Length - 1)
            {
                value /= 1024;
                unitIndex++;
            }
            return $"{value:0.#} {units[unitIndex]}";
        }
    }
}
