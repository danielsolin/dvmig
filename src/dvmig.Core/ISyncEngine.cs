using Microsoft.Xrm.Sdk;

namespace dvmig.Core
{
    /// <summary>
    /// Defines the contract for an engine that synchronizes data records 
    /// between a source and target Dataverse environment.
    /// </summary>
    public interface ISyncEngine
    {
        /// <summary>
        /// Synchronizes a collection of entity records.
        /// </summary>
        /// <param name="entities">The collection of entities to migrate.</param>
        /// <param name="options">
        /// Configuration options for the synchronization process.
        /// </param>
        /// <param name="progress">
        /// An optional progress reporter for status messages.
        /// </param>
        /// <param name="recordProgress">
        /// An optional progress reporter for boolean success/failure per 
        /// record.
        /// </param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>
        /// A task representing the asynchronous synchronization operation.
        /// </returns>
        Task SyncAsync(
            IEnumerable<Entity> entities,
            SyncOptions options,
            IProgress<string>? progress = null,
            IProgress<bool>? recordProgress = null,
            CancellationToken cancellationToken = default
        );

        /// <summary>
        /// Synchronizes a single entity record from the source to the 
        /// target environment.
        /// </summary>
        /// <param name="entity">The entity to synchronize.</param>
        /// <param name="options">
        /// Configuration options for the synchronization process.
        /// </param>
        /// <param name="progress">
        /// An optional progress reporter for status messages.
        /// </param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>
        /// True if the synchronization was successful, otherwise false.
        /// </returns>
        Task<bool> SyncRecordAsync(
            Entity entity,
            SyncOptions options,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default
        );
    }

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
    }
}



