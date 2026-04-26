using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace dvmig.Core.Synchronization
{
    public partial class SyncEngine
    {
        /// <inheritdoc />
        public async Task<Microsoft.Xrm.Sdk.Query.ColumnSet> GetValidColumnsAsync(
            string logicalName,
            CancellationToken ct = default
        )
        {
            return await _metadataCache.GetValidColumnsAsync(logicalName, ct);
        }

        /// <summary>
        /// Prepares a source entity for the target environment by mapping
        /// users, resolving lookups through the ID cache, and stripping
        /// forbidden or invalid attributes.
        /// </summary>
        /// <param name="entity">The source entity record.</param>
        /// <param name="metadata">The entity metadata from the target.</param>
        /// <param name="options">The synchronization configuration.</param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>A prepared entity record ready for the target.</returns>
        private async Task<Entity> PrepareEntityForTargetAsync(
            Entity entity,
            EntityMetadata metadata,
            SyncOptions options,
            CancellationToken ct)
        {
            return await _entityPreparer.PrepareEntityForTargetAsync(
                entity,
                metadata,
                options,
                _userMapper,
                _idMappingCache,
                ct
            );
        }

        /// <summary>
        /// Attempts to find a record on the target environment that matches
        /// the source record based on its primary name or business key.
        /// </summary>
        /// <param name="entity">The source entity record.</param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>
        /// The ID of the matching record on the target, or null if not found.
        /// </returns>
        private async Task<Guid?> FindExistingOnTargetAsync(
            Entity entity,
            CancellationToken ct)
        {
            return await _entityPreparer.FindExistingOnTargetAsync(
                entity,
                _target,
                _metadataCache.GetMetadataAsync,
                ct
            );
        }

        /// <summary>
        /// Retrieves entity metadata from the target environment, utilizing
        /// an internal cache to improve performance.
        /// </summary>
        /// <param name="logicalName">The logical name of the entity.</param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>The entity metadata, or null if it cannot be retrieved.</returns>
        private async Task<EntityMetadata?> GetMetadataAsync(
            string logicalName,
            CancellationToken ct)
        {
            return await _metadataCache.GetMetadataAsync(logicalName, ct);
        }

        /// <summary>
        /// Determines whether an attribute is forbidden from being synchronized
        /// (e.g., system-managed fields like versionnumber).
        /// </summary>
        /// <param name="attrName">The logical name of the attribute.</param>
        /// <returns>True if the attribute is forbidden; otherwise, false.</returns>
        private bool IsForbiddenAttribute(string attrName)
        {
            return _entityPreparer.IsForbiddenAttribute(attrName);
        }

        /// <summary>
        /// Determines whether an attribute is a user reference field (e.g.,
        /// ownerid, createdby).
        /// </summary>
        /// <param name="attrName">The logical name of the attribute.</param>
        /// <returns>True if the attribute is a user field; otherwise, false.</returns>
        private bool IsUserAttribute(string attrName)
        {
            return _entityPreparer.IsUserAttribute(attrName);
        }
    }
}
