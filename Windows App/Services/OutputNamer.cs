using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Voxa.Services
{
    /// <summary>
    /// Expands a user-facing filename pattern (e.g. "Interview_{n3}" or "{name}_clean")
    /// into an actual output filename for one file in a batch. Kept separate from
    /// MainViewModel so the token logic is easy to unit test and reuse.
    /// </summary>
    public static class OutputNamer
    {
        private static readonly Regex InvalidChars =
            new(@"[""<>|:\*\?\\/\x00-\x1F]", RegexOptions.Compiled);

        /// <summary>
        /// Builds the output filename (without extension) for one file.
        /// </summary>
        /// <param name="pattern">Pattern containing {name}, {n}, {n2}, {n3}, {n4}.</param>
        /// <param name="originalFilePath">Full path to the source file.</param>
        /// <param name="sequenceNumber">This file's position in the batch (already offset by SequenceStart).</param>
        public static string BuildFileName(string pattern, string originalFilePath, int sequenceNumber)
        {
            var originalName = Path.GetFileNameWithoutExtension(originalFilePath);
            var effectivePattern = string.IsNullOrWhiteSpace(pattern) ? "{name}" : pattern;

            var result = effectivePattern
                .Replace("{name}", originalName, StringComparison.OrdinalIgnoreCase)
                .Replace("{n4}", sequenceNumber.ToString("D4", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
                .Replace("{n3}", sequenceNumber.ToString("D3", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
                .Replace("{n2}", sequenceNumber.ToString("D2", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
                .Replace("{n}", sequenceNumber.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);

            result = InvalidChars.Replace(result, "").Trim();

            // An empty or pattern-only-whitespace result would produce an unusable
            // filename (or collide every file into the same name) - fall back to the
            // original name rather than let that happen silently.
            return string.IsNullOrWhiteSpace(result) ? originalName : result;
        }

        /// <summary>Live preview text shown next to the pattern field, using a sample filename.</summary>
        public static string PreviewExample(string pattern, int sequenceStart)
        {
            var sample = BuildFileName(pattern, "my_recording.wav", sequenceStart);
            return sample;
        }
    }
}
