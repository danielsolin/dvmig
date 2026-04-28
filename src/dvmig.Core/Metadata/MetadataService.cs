using dvmig.Core.Interfaces;
using dvmig.Core.Providers;
using dvmig.Core.Shared;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;

namespace dvmig.Core.Metadata
{
   /// <summary>
   /// Defines the contract for a service that retrieves and filters 
   /// Dataverse entity metadata.
   /// </summary>
   public interface IMetadataService
   {
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
   }

   /// <summary>
   /// Implementation of the metadata service that retrieves and filters 
   /// entities using standard dvmig rules.
   /// </summary>
   public class MetadataService : IMetadataService
   {
      /// <inheritdoc />
      public async Task<List<EntityMetadata>> GetMigrationEntitiesAsync(
          IDataverseProvider provider,
          CancellationToken ct = default
      )
      {
         var request = new RetrieveAllEntitiesRequest
         {
            EntityFilters = EntityFilters.Entity,
            RetrieveAsIfPublished = true
         };

         var response = (RetrieveAllEntitiesResponse)await
             provider.ExecuteAsync(request, ct);

         return response.EntityMetadata
             .Where(e =>
                 (e.IsCustomEntity == true ||
                  EntityMetadataHelper.IsStandardEntity(e.LogicalName)) &&
                 e.IsIntersect == false &&
                 e.IsValidForAdvancedFind == true &&
                 !string.IsNullOrEmpty(
                     e.DisplayName?.UserLocalizedLabel?.Label))
             .OrderBy(e =>
                 e.DisplayName?.UserLocalizedLabel?.Label ??
                 e.LogicalName)
             .ToList();
      }

      /// <inheritdoc />
      public async Task<long> GetRecordCountAsync(
          IDataverseProvider provider,
          string logicalName,
          CancellationToken ct = default
      )
      {
         return await provider.GetRecordCountAsync(logicalName, ct);
      }
   }
}
