using dvmig.Core.Interfaces;
using dvmig.Core.Shared;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using System.ServiceModel;

namespace dvmig.Core.Providers
{
   /// <summary>
   /// Provides extension methods for <see cref="IDataverseProvider"/>.
   /// </summary>
   public static class DataverseProviderExtensions
   {
      /// <summary>
      /// Determines whether the specified exception indicates that 
      /// the requested record or object does not exist in Dataverse.
      /// </summary>
      /// <param name="ex">The exception to check.</param>
      /// <returns>True if it is a "Not Found" exception.</returns>
      public static bool IsNotFoundException(this Exception ex)
      {
         if (ex is not FaultException faultEx)
            return false;

         var doesNotExist = SystemConstants.ErrorCodes.DoesNotExist;
         var comparison = StringComparison.OrdinalIgnoreCase;

         return faultEx.Message.Contains(doesNotExist) ||
                faultEx.Message.Contains(
                   SystemConstants.ErrorKeywords.DoesNotExist,
                   comparison
                );
      }

      /// <summary>
      /// Retrieves the primary ID attribute name for a given 
      /// entity logical name.
      /// </summary>
      /// <param name="provider">The Dataverse provider.</param>
      /// <param name="logicalName">The logical name of the entity.</param>
      /// <param name="ct">A cancellation token.</param>
      /// <returns>The primary ID attribute name, or null.</returns>
      public static async Task<string?> GetPrimaryIdAttributeAsync(
          this IDataverseProvider provider,
          string logicalName,
          CancellationToken ct = default
      )
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
