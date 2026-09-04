namespace RimageGui.Models
{
    /// <summary>Lifecycle state of one row in the file table.</summary>
    public enum FileStatus
    {
        Pending,
        Running,
        Done,
        Failed,
        Skipped
    }
}
