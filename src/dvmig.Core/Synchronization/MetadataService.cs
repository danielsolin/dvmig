using dvmig.Core.Interfaces;
using dvmig.Core.Shared;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;

namespace dvmig.Core.Synchronization
{
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
                 (e.IsCustomEntity == true) &&
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
