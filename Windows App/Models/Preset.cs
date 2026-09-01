namespace Voxa.Models
{
    /// <summary>A named, reusable ProcessingParameters configuration, e.g. "Podcast format".</summary>
    public class Preset
    {
        public string Name { get; set; } = string.Empty;
        public ProcessingParameters Parameters { get; set; } = new();

        public override string ToString() => Name;
    }
}
