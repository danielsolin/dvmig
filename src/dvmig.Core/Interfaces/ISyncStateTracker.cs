namespace dvmig.Core.Interfaces
{
    /// <summary>
    /// Defines the contract for a component that tracks the synchronization 
    /// state of records to allow resuming interrupted jobs.
    /// </summary>
    public interface ISyncStateTracker
    {
        /// <summary>
        /// Initializes the tracker for a specific migration job and 
        /// entity type.
        /// </summary>
        /// <param name="sourceKey">
        /// A unique identifier for the source environment.
        /// </param>
        /// <param name="targetKey">
        /// A unique identifier for the target environment.
        /// </param>
        /// <param name="logicalName">
        /// The logical name of the entity being synced.
        /// </param>
        Task InitializeAsync(
            string sourceKey,
            string targetKey,
            string logicalName
        );

        /// <summary>
        /// Gets the set of IDs that have already been successfully 
        /// synchronized.
        /// </summary>
        /// <returns>A hash set of synced record IDs.</returns>
        Task<HashSet<Guid>> GetSyncedIdsAsync();

        /// <summary>
        /// Marks a specific record ID as successfully synchronized.
        /// </summary>
        /// <param name="logicalName">The logical name of the entity.</param>
        /// <param name="id">The ID of the record.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task MarkAsSyncedAsync(string logicalName, Guid id);

        /// <summary>
        /// Clears the persisted synchronization state for the current 
        /// entity job.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task ClearStateAsync();

        /// <summary>
        /// Checks if a previous state exists for the current entity job.
        /// </summary>
        /// <returns>True if a state file exists; otherwise, false.</returns>
        bool StateExists();
    }
}
