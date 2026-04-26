using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace dvmig.Core.Synchronization
{
    /// <summary>
    /// Defines the contract for caching and retrieving entity metadata.
    /// </summary>
    public interface IMetadataCache
    {
        /// <summary>
        /// Retrieves entity metadata from the target environment, utilizing
        /// an internal cache to improve performance.
        /// </summary>
        /// <param name="entityLogicalName">The logical name of the entity.</param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>The entity metadata, or null if it cannot be retrieved.</returns>
        Task<EntityMetadata?> GetMetadataAsync(
            string entityLogicalName,
            CancellationToken ct = default);

        /// <summary>
        /// Retrieves a set of valid attributes for extraction, excluding
        /// logical or non-readable columns to optimize query performance.
        /// </summary>
        /// <param name="logicalName">The logical name of the entity.</param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>A configured ColumnSet containing valid attributes.</returns>
        Task<ColumnSet> GetValidColumnsAsync(
            string logicalName,
            CancellationToken ct = default);

        /// <summary>
        /// Clears the internal metadata cache.
        /// </summary>
        void ClearCache();
    }
}
