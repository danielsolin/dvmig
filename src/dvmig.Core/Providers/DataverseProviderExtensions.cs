using System.ServiceModel;
using System.Text.RegularExpressions;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using dvmig.Core.Interfaces;
using dvmig.Core.Shared;

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

         return faultEx.Message.IndexOf(doesNotExist, comparison) >= 0 ||
                faultEx.Message.IndexOf(
                   SystemConstants.ErrorKeywords.DoesNotExist,
                   comparison
                ) >= 0;
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

         var metaResponse = (RetrieveEntityResponse)await provider
            .ExecuteAsync(request, ct);

         return metaResponse.EntityMetadata.PrimaryIdAttribute;
      }

      /// <summary>
      /// Gets the total record count for a specific entity type, optionally 
      /// filtered by a query.
      /// </summary>
      /// <param name="provider">The Dataverse provider.</param>
      /// <param name="entityName">The logical name of the entity.</param>
      /// <param name="query">Optional query expression for filtering.</param>
      /// <param name="ct">A cancellation token.</param>
      /// <returns>The total number of records.</returns>
      public static async Task<long> GetRecordCountAsync(
         this IDataverseProvider provider,
         string entityName,
         QueryExpression? query = null,
         CancellationToken ct = default
      )
      {
         var primaryId = await provider.GetPrimaryIdAttributeAsync(
            entityName,
            ct
         ) ?? $"{entityName}id";

         string fetchXml;

         if (query != null)
         {
            var conversionRequest = new QueryExpressionToFetchXmlRequest
            {
               Query = query
            };

            var conversionResponse = (QueryExpressionToFetchXmlResponse)
               await provider.ExecuteAsync(conversionRequest, ct);

            fetchXml = conversionResponse.FetchXml;

            // Remove existing attributes to make it an aggregate query
            fetchXml = Regex.Replace(fetchXml, "<attribute[^>]+?/>", "");

            fetchXml = Regex.Replace(
               fetchXml,
               "<attribute[^>]+?>.*?</attribute>",
               "",
               RegexOptions.Singleline
            );

            // Remove order clauses as they are not allowed in aggregate queries
            fetchXml = Regex.Replace(fetchXml, "<order[^>]+?/>", "");

            fetchXml = fetchXml.Replace("<fetch", "<fetch aggregate='true'");

            fetchXml = Regex.Replace(
               fetchXml,
               $"(<entity name=[\"']{entityName}[\"']>)",
               $"$1<attribute name='{primaryId}' alias='count' aggregate='count' />",
               RegexOptions.IgnoreCase
            );
         }
         else
         {
            fetchXml = $@"
               <fetch aggregate='true'>
                 <entity name='{entityName}'>
                   <attribute name='{primaryId}' alias='count' aggregate='count' />
                 </entity>
               </fetch>";
         }

         var result = await provider.RetrieveMultipleAsync(
            new FetchExpression(fetchXml),
            ct
         );

         if (result.Entities.Count > 0 &&
             result.Entities[0].Contains("count"))
         {
            var entity = result.Entities[0];
            var aliasedValue = (AliasedValue)entity["count"];

            return Convert.ToInt64(aliasedValue.Value);
         }

         return 0;
      }
   }
}
