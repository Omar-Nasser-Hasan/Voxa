using System.Collections.Generic;
using Voxa.Models;

namespace Voxa.Services
{
    public static class PresetCatalog
    {
        public static IEnumerable<Preset> CreateBuiltInPresets() => new[]
        {
            new Preset { LocalizationKey = "Preset.Podcast", IsBuiltIn = true, Parameters = new ProcessingParameters { OutputFormat = "mp3", SampleRateHz = 44100, BitrateKbps = 192, NormalizeVolume = true, EnhanceClarity = true } },
            new Preset { LocalizationKey = "Preset.VoiceNote", IsBuiltIn = true, Parameters = new ProcessingParameters { OutputFormat = "m4a", SampleRateHz = 44100, BitrateKbps = 128, NormalizeVolume = true, EnhanceClarity = true, TrimSilence = true } }
        };
    }
}
