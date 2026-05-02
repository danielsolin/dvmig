using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace dvmig.Core.Interfaces
{
   /// <summary>
   /// Defines the contract for a service that retrieves, filters, and 
   /// caches Dataverse entity metadata.
   /// </summary>
   public interface IMetadataService
   {
      /// <summary>
      /// Retrieves entity metadata from the target environment, utilizing
      /// an internal cache to improve performance.
      /// </summary>
      /// <param name="entityLogicalName">
      /// The logical name of the entity.
      /// </param>
      /// <param name="ct">A cancellation token.</param>
      /// <returns>
      /// The entity metadata, or null if it cannot be retrieved.
      /// </returns>
      Task<EntityMetadata?> GetMetadataAsync(
         string entityLogicalName,
         CancellationToken ct = default
      );

      /// <summary>
      /// Retrieves a set of valid attributes for extraction, excluding
      /// logical or non-readable columns to optimize query performance.
      /// </summary>
      /// <param name="logicalName">
      /// The logical name of the entity.
      /// </param>
      /// <param name="ct">A cancellation token.</param>
      /// <returns>
      /// A configured ColumnSet containing valid attributes.
      /// </returns>
      Task<ColumnSet> GetValidColumnsAsync(
         string logicalName,
         CancellationToken ct = default
      );

      /// <summary>
      /// Retrieves all standard and custom entity metadata from the 
      /// environment, filtered for migration suitability.
      /// </summary>
      /// <param name="provider">The Dataverse provider to use.</param>
      /// <param name="ct">A cancellation token.</param>
      /// <returns>A sorted list of migration-ready entity metadata.</returns>
      Task<List<EntityMetadata>> GetMigrationEntitiesAsync(
         IDataverseProvider provider,
         CancellationToken ct = default
      );

      /// <summary>
      /// Gets the total number of records for a specific entity in the 
      /// environment.
      /// </summary>
      /// <param name="provider">The Dataverse provider to use.</param>
      /// <param name="logicalName">The logical name of the entity.</param>
      /// <param name="ct">A cancellation token.</param>
      /// <returns>The total number of records.</returns>
      Task<long> GetRecordCountAsync(
         IDataverseProvider provider,
         string logicalName,
         CancellationToken ct = default
      );

      /// <summary>
      /// Clears the internal metadata cache.
      /// </summary>
      void ClearCache();
   }
}
