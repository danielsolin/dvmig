namespace dvmig.App.Models
{
    /// <summary>
    /// Configuration for a specific entity migration, specifying whether to 
    /// sync all records or a selected subset.
    /// </summary>
    public class EntitySyncConfiguration
    {
        /// <summary>
        /// Gets the logical name of the entity.
        /// </summary>
        public string LogicalName { get; }

        /// <summary>
        /// Gets or sets a value indicating whether all records should be 
        /// synchronized for this entity.
        /// </summary>
        public bool SyncAllRecords { get; set; } = true;

        /// <summary>
        /// Gets the collection of specific record IDs to synchronize when 
        /// <see cref="SyncAllRecords"/> is false.
        /// </summary>
        public HashSet<Guid> SelectedRecordIds { get; } = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="EntitySyncConfiguration"/> 
        /// class.
        /// </summary>
        /// <param name="logicalName">The logical name of the entity.</param>
        public EntitySyncConfiguration(string logicalName)
        {
            LogicalName = logicalName;
        }
    }
}
