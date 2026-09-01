using System;
using System.IO;
using System.Linq;

namespace Voxa.Services
{
    public static class AudioFileFilter
    {
        public static readonly string[] SupportedInputExtensions =
        {
            ".mp3", ".wav", ".m4a", ".aac", ".flac", ".ogg", ".wma", ".opus", ".aiff", ".alac"
        };

        public static readonly string[] SupportedOutputFormats =
        {
            "mp3", "wav", "m4a", "flac", "ogg", "aac"
        };

        public static bool IsSupported(string filePath)
        {
            var ext = Path.GetExtension(filePath);
            if (string.IsNullOrEmpty(ext)) return false;
            return SupportedInputExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Builds an OpenFileDialog-compatible filter string.</summary>
        public static string OpenFileDialogFilter()
        {
            var exts = string.Join(";", SupportedInputExtensions.Select(e => "*" + e));
            return $"Audio Files ({exts})|{exts}|All Files (*.*)|*.*";
        }
    }
}
