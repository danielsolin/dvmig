namespace dvmig.Core.Synchronization
{
    /// <summary>
    /// Represents a recorded migration failure.
    /// </summary>
    public class MigrationFailureRecord
    {
        public Guid Id { get; set; }
        public string EntityLogicalName { get; set; } = string.Empty;
        public string SourceId { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public DateTime TimestampUtc { get; set; }
    }
}
