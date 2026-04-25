using Microsoft.Xrm.Sdk;

namespace dvmig.Core.Synchronization
{
    /// <summary>
    /// Defines the contract for an engine that synchronizes data records 
    /// between a source and target Dataverse environment.
    /// </summary>
    public interface ISyncEngine
    {
        /// <summary>
        /// Synchronizes a collection of entities from the source to the target.
        /// </summary>
        /// <param name="entities">The collection of entities to sync.</param>
        /// <param name="options">The synchronization configuration options.</param>
        /// <param name="progress">An optional progress reporter for log messages.</param>
        /// <param name="recordProgress">
        /// An optional progress reporter for individual record success/failure.
        /// </param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>A task representing the asynchronous sync operation.</returns>
        Task SyncAsync(
            IEnumerable<Entity> entities,
            SyncOptions options,
            IProgress<string>? progress = null,
            IProgress<bool>? recordProgress = null,
            CancellationToken ct = default
        );

        /// <summary>
        /// Synchronizes a single entity record from the source to the target.
        /// </summary>
        /// <param name="entity">The entity record to synchronize.</param>
        /// <param name="options">The synchronization configuration options.</param>
        /// <param name="progress">An optional progress reporter.</param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>
        /// True if the synchronization was successful; otherwise, false.
        /// </returns>
        Task<bool> SyncRecordAsync(
            Entity entity,
            SyncOptions options,
            IProgress<string>? progress = null,
            CancellationToken ct = default
        );
    }
}
