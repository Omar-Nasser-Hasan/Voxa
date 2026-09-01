using System.Collections.Generic;
using System.Linq;
using Voxa.Models;

namespace Voxa.Services
{
    /// <summary>
    /// Central place for "is this value sane" checks, used before a batch run starts
    /// and before a preset is saved, so bad values are caught with a clear message
    /// instead of surfacing as a cryptic FFmpeg failure per file.
    /// </summary>
    public static class ParameterValidator
    {
        public const int MinSampleRate = 8000;
        public const int MaxSampleRate = 192000;
        public const double MinVolumeDb = -30;
        public const double MaxVolumeDb = 30;
        public const double MinSpeed = 0.25;
        public const double MaxSpeed = 4.0;
        public const int MinBitrate = 32;
        public const int MaxBitrate = 320;
        public const double MinSilencePaddingSec = 0;
        public const double MaxSilencePaddingSec = 30;

        public static List<string> Validate(ProcessingParameters p)
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(p.OutputFormat) ||
                !AudioFileFilter.SupportedOutputFormats.Contains(p.OutputFormat.ToLowerInvariant()))
            {
                errors.Add($"'{p.OutputFormat}' is not a supported output format.");
            }

            if (!p.KeepOriginalSampleRate && (p.SampleRateHz < MinSampleRate || p.SampleRateHz > MaxSampleRate))
            {
                errors.Add($"Sample rate must be between {MinSampleRate:N0} Hz and {MaxSampleRate:N0} Hz.");
            }

            if (p.VolumeChangeDb < MinVolumeDb || p.VolumeChangeDb > MaxVolumeDb)
            {
                errors.Add($"Volume change must be between {MinVolumeDb} dB and {MaxVolumeDb} dB.");
            }

            if (p.SpeedMultiplier < MinSpeed || p.SpeedMultiplier > MaxSpeed)
            {
                errors.Add($"Speed must be between {MinSpeed}x and {MaxSpeed}x.");
            }

            if (p.BitrateKbps < MinBitrate || p.BitrateKbps > MaxBitrate)
            {
                errors.Add($"Bitrate must be between {MinBitrate} kbps and {MaxBitrate} kbps.");
            }

            if (p.SilencePaddingStartSec < MinSilencePaddingSec || p.SilencePaddingStartSec > MaxSilencePaddingSec)
            {
                errors.Add($"Starting silence must be between {MinSilencePaddingSec} and {MaxSilencePaddingSec} seconds.");
            }

            if (p.SilencePaddingEndSec < MinSilencePaddingSec || p.SilencePaddingEndSec > MaxSilencePaddingSec)
            {
                errors.Add($"Ending silence must be between {MinSilencePaddingSec} and {MaxSilencePaddingSec} seconds.");
            }

            return errors;
        }
    }
}
