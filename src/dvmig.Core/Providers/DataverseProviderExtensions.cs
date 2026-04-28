using dvmig.Core.Interfaces;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;

namespace dvmig.Core.Providers
{
   /// <summary>
   /// Provides extension methods for <see cref="IDataverseProvider"/>.
   /// </summary>
   public static class DataverseProviderExtensions
   {
      /// <summary>
      /// Retrieves the primary ID attribute name for a given entity logical name.
      /// </summary>
      /// <param name="provider">The Dataverse provider.</param>
      /// <param name="logicalName">The logical name of the entity.</param>
      /// <param name="ct">A cancellation token.</param>
      /// <returns>The primary ID attribute name, or null.</returns>
      public static async Task<string?> GetPrimaryIdAttributeAsync(
          this IDataverseProvider provider,
          string logicalName,
          CancellationToken ct = default)
      {
         var request = new RetrieveEntityRequest
         {
            LogicalName = logicalName,
            EntityFilters = EntityFilters.Entity
         };

         var metaResponse = (RetrieveEntityResponse)await
             provider.ExecuteAsync(request, ct);

         return metaResponse.EntityMetadata.PrimaryIdAttribute;
      }
   }
}
