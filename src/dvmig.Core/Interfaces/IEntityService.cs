using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using System.Collections.Concurrent;

namespace dvmig.Core.Interfaces
{
   /// <summary>
   /// Defines the contract for a service that prepares entities
   /// for synchronization to the target environment.
   /// </summary>
   public interface IEntityService
   {
      /// <summary>
      /// Prepares a source entity for creation or update in the target 
      /// environment. This includes mapping lookups, stripping read-only 
      /// fields, and handling complex types like activity parties.
      /// </summary>
      /// <param name="sourceEntity">The original entity from the source.</param>
      /// <param name="metadata">Metadata for the entity type.</param>
      /// <param name="options">Synchronization options.</param>
      /// <param name="userResolver">The user resolver to use for mapping.</param>
      /// <param name="idMappingCache">A cache of already mapped record IDs.</param>
      /// <param name="ct">A cancellation token.</param>
      /// <returns>A prepared entity ready for the target environment.</returns>
      Task<Entity> PrepareEntityForTargetAsync(
         Entity sourceEntity,
         EntityMetadata metadata,
         dvmig.Core.Synchronization.SyncOptions options,
         IUserResolver userResolver,
         ConcurrentDictionary<string, Guid> idMappingCache,
         CancellationToken ct = default
      );

      /// <summary>
      /// Attempts to find a matching record on the target environment using 
      /// the primary name attribute as a business key.
      /// </summary>
      /// <param name="entity">The entity to find a match for.</param>
      /// <param name="target">The target Dataverse provider.</param>
      /// <param name="getMetadataFunc">
      /// Function to retrieve entity metadata.
      /// </param>
      /// <param name="ct">A cancellation token.</param>
      /// <returns>
      /// The ID of the existing record on the target, or null if not found.
      /// </returns>
      Task<Guid?> FindExistingOnTargetAsync(
         Entity entity,
         IDataverseProvider target,
         Func<string, CancellationToken, Task<EntityMetadata?>> 
            getMetadataFunc,
         CancellationToken ct = default
      );

      /// <summary>
      /// Checks if an attribute is forbidden for create/update operations.
      /// </summary>
      /// <param name="attributeName">The logical name of the attribute.</param>
      /// <returns>True if the attribute is forbidden; otherwise, false.</returns>
      bool IsForbiddenAttribute(string attributeName);

      /// <summary>
      /// Checks if an attribute is a user-reference attribute.
      /// </summary>
      /// <param name="attributeName">The logical name of the attribute.</param>
      /// <returns>True if it is a user attribute; otherwise, false.</returns>
      bool IsUserAttribute(string attributeName);
   }
}
