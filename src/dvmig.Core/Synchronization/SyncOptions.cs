namespace dvmig.Core.Synchronization
{
    /// <summary>
    /// Contains configuration options that dictate the behavior of the
    /// synchronization process.
    /// </summary>
    public class SyncOptions
    {
        /// <summary>
        /// Gets or sets a value indicating whether to skip synchronization
        /// for records that already exist in the target.
        /// </summary>
        public bool SkipExisting { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether to preserve original
        /// creation and modification dates.
        /// </summary>
        public bool PreserveDates { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether to override the
        /// "Created By" and "Modified By" users.
        /// </summary>
        public bool OverrideCreatedBy { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether to automatically strip
        /// unresolvable lookups and retry the sync.
        /// </summary>
        public bool StripMissingDependencies { get; set; } = false;

        /// <summary>
        /// Gets or sets the maximum number of concurrent requests to the
        /// target environment.
        /// </summary>
        public int MaxDegreeOfParallelism { get; set; } = 10;
    }
}
