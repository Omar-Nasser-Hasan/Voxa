using System;

namespace Voxa.Models
{
    /// <summary>
    /// Plain-data snapshot of every processing option. This is what gets saved
    /// into a preset and what gets handed to FFmpegService to build the
    /// actual FFmpeg command line.
    /// </summary>
    public class ProcessingParameters : ICloneable
    {
        /// <summary>Output container/extension, lowercase, no dot. e.g. "mp3", "wav", "m4a".</summary>
        public string OutputFormat { get; set; } = "mp3";

        /// <summary>Target sample rate in Hz. Ignored when KeepOriginalSampleRate is true.</summary>
        public int SampleRateHz { get; set; } = 44100;

        /// <summary>When true, each file's own sample rate is left untouched.</summary>
        public bool KeepOriginalSampleRate { get; set; } = false;

        /// <summary>Gain change in dB. 0 = no change. Negative = quieter, positive = louder.</summary>
        public double VolumeChangeDb { get; set; } = 0;

        /// <summary>Light denoise + presence boost, aimed at muffled/noisy speech recordings.</summary>
        public bool EnhanceClarity { get; set; } = false;

        /// <summary>Loudness normalization (EBU R128 via FFmpeg's loudnorm filter).</summary>
        public bool NormalizeVolume { get; set; } = false;

        /// <summary>Playback speed multiplier. 1.0 = unchanged. Pitch is preserved (uses atempo).</summary>
        public double SpeedMultiplier { get; set; } = 1.0;

        /// <summary>Output bitrate in kbps, used for lossy formats (mp3/aac/ogg).</summary>
        public int BitrateKbps { get; set; } = 192;

        /// <summary>Seconds of silence added to the very start of each output file. 0 = none.</summary>
        public double SilencePaddingStartSec { get; set; } = 0;

        /// <summary>Seconds of silence added to the very end of each output file. 0 = none.</summary>
        public double SilencePaddingEndSec { get; set; } = 0;

        /// <summary>Removes sustained quiet audio at the beginning and end of each source file.</summary>
        public bool TrimSilence { get; set; } = false;

        /// <summary>When enabled, the same tags are written to each processed file.</summary>
        public bool WriteMetadata { get; set; } = false;
        public string MetadataTitle { get; set; } = string.Empty;
        public string MetadataArtist { get; set; } = string.Empty;
        public string MetadataAlbum { get; set; } = string.Empty;

        /// <summary>
        /// Output filename pattern. Supports the tokens {name} (original filename, no
        /// extension), {n} (sequence number, no padding), {n2}/{n3}/{n4} (zero-padded
        /// sequence number). Extension is always added separately based on OutputFormat.
        /// Defaults to keeping the original name unchanged.
        /// </summary>
        public string FileNamePattern { get; set; } = "{name}";

        /// <summary>When false, output files keep their original base filename.</summary>
        public bool UseCustomFileNames { get; set; } = true;

        /// <summary>Starting value for the {n}/{n2}/{n3}/{n4} sequence-number tokens.</summary>
        public int SequenceStart { get; set; } = 1;

        public object Clone() => MemberwiseClone();
    }
}
