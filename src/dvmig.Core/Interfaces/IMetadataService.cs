using dvmig.Core.Interfaces;
using dvmig.Core.Shared;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;

namespace dvmig.Core.Interfaces
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
}
