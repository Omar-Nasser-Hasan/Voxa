namespace Voxa.Models
{
    /// <summary>Per-file state shown in the batch queue's Status column.</summary>
    public enum ProcessingStatus
    {
        Pending,
        Processing,
        Success,
        Failed,
        Skipped
    }
}
