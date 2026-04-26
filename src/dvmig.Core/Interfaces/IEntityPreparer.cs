using System.Collections.Concurrent;
using dvmig.Core.Synchronization;
using dvmig.Providers;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;

namespace dvmig.Core.Interfaces
{
    /// <summary>
    /// Defines the contract for preparing entities for synchronization to the
    /// target environment.
    /// </summary>
    public interface IEntityPreparer
    {
        /// <summary>
        /// Prepares a source entity for the target environment by mapping
        /// users, resolving lookups through the ID cache, and stripping
        /// forbidden or invalid attributes.
        /// </summary>
        /// <param name="sourceEntity">The source entity record.</param>
        /// <param name="metadata">The entity metadata from the target.</param>
        /// <param name="options">The synchronization configuration.</param>
        /// <param name="userMapper">The user mapping service.</param>
        /// <param name="idMappingCache">
        /// A cache for storing ID mappings between source and target.
        /// </param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>A prepared entity record ready for the target.</returns>
        Task<Entity> PrepareEntityForTargetAsync(
            Entity sourceEntity,
            EntityMetadata metadata,
            SyncOptions options,
            IUserMapper userMapper,
            ConcurrentDictionary<string, Guid> idMappingCache,
            CancellationToken ct = default);

        /// <summary>
        /// Attempts to find a record on the target environment that matches
        /// the source record based on its primary name or business key.
        /// </summary>
        /// <param name="entity">The source entity record.</param>
        /// <param name="target">The target Dataverse provider.</param>
        /// <param name="getMetadataFunc">
        /// A function that retrieves entity metadata.
        /// </param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>
        /// The ID of the matching record on the target, or null if not found.
        /// </returns>
        Task<Guid?> FindExistingOnTargetAsync(
            Entity entity,
            IDataverseProvider target,
            Func<string, CancellationToken, Task<EntityMetadata?>> getMetadataFunc,
            CancellationToken ct = default);

        /// <summary>
        /// Determines whether an attribute is forbidden from being synchronized
        /// (e.g., system-managed fields like versionnumber).
        /// </summary>
        /// <param name="attributeName">The logical name of the attribute.</param>
        /// <returns>True if the attribute is forbidden; otherwise, false.</returns>
        bool IsForbiddenAttribute(string attributeName);

        /// <summary>
        /// Determines whether an attribute is a user reference field (e.g.,
        /// ownerid, createdby).
        /// </summary>
        /// <param name="attributeName">The logical name of the attribute.</param>
        /// <returns>True if the attribute is a user field; otherwise, false.</returns>
        bool IsUserAttribute(string attributeName);
    }
}
