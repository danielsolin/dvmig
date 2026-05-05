using System.Collections.Concurrent;
using dvmig.Core.Synchronization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace dvmig.Core.Interfaces
{
   /// <summary>
   /// Defines the contract for a service that prepares entities
   /// for synchronization to the target environment, handles metadata,
   /// and record associations.
   /// </summary>
   public interface IEntityService
   {
      #region Preparation & Mapping

      /// <summary>
      /// Prepares a source entity for creation or update in the target 
      /// environment.
      /// </summary>
      Task<Entity> PrepareEntityForTargetAsync(
         Entity sourceEntity,
         EntityMetadata metadata,
         SyncOptions options,
         IUserResolver userResolver,
         ConcurrentDictionary<string, Guid> idMappingCache,
         CancellationToken ct = default
      );

      /// <summary>
      /// Attempts to find a matching record on the target environment.
      /// </summary>
      Task<Guid?> FindExistingOnTargetAsync(
         Entity entity,
         IDataverseProvider target,
         CancellationToken ct = default
      );

      /// <summary>
      /// Checks if an attribute is forbidden for create/update operations.
      /// </summary>
      bool IsForbiddenAttribute(string attributeName);

      /// <summary>
      /// Checks if an attribute is a user-reference attribute.
      /// </summary>
      bool IsUserAttribute(string attributeName);

      /// <summary>
      /// Retrieves all record IDs for a specific entity from a provider.
      /// </summary>
      Task<HashSet<Guid>> GetAllIdsAsync(
         IDataverseProvider provider,
         string logicalName,
         CancellationToken ct = default
      );

      #endregion

      #region Metadata & Querying

      /// <summary>
      /// Retrieves metadata for a specific entity type, using a cache 
      /// if possible.
      /// </summary>
      Task<EntityMetadata?> GetMetadataAsync(
         string entityLogicalName,
         CancellationToken ct = default
      );

      /// <summary>
      /// Generates a ColumnSet containing only the attributes that are valid
      /// to retrieve for synchronization.
      /// </summary>
      Task<ColumnSet> GetValidColumnsAsync(
         string logicalName,
         CancellationToken ct = default
      );

      /// <summary>
      /// Retrieves a list of all entities that are suitable for migration.
      /// </summary>
      Task<List<EntityMetadata>> GetMigrationEntitiesAsync(
         IDataverseProvider provider,
         CancellationToken ct = default
      );

      /// <summary>
      /// Clears the internal metadata cache.
      /// </summary>
      void ClearMetadataCache();

      #endregion

      #region Relationships (N:N)

      /// <summary>
      /// Associates an N:N intersect record in the target environment.
      /// </summary>
      Task AssociateAsync(
         IDataverseProvider target,
         Entity entity,
         CancellationToken ct = default,
         Guid? callerId = null
      );

      #endregion
   }
}
