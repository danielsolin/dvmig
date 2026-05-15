using dvmig.Core.Interfaces;
using Microsoft.Crm.Sdk.Messages;

namespace dvmig.Core.Providers
{
   /// <summary>
   /// Factory for creating the appropriate <see cref="IDataverseProvider"/>
   /// based on the connection string and environment type.
   /// </summary>
   public static class ProviderFactory
   {
      /// <summary>
      /// Creates a provider, automatically detecting whether to use the 
      /// modern or legacy implementation.
      /// </summary>
      /// <param name="connectionString">The connection string.</param>
      /// <returns>A connected provider.</returns>
      public static async Task<IDataverseProvider> CreateAsync(
         string connectionString
      )
      {
         // 1. Check for obvious legacy markers in the string
         if (IsObviousLegacy(connectionString))
            return new LegacyCrmProvider(connectionString);

         try
         {
            // 2. Try the modern provider first
            var modern = new DataverseProvider(connectionString);

            // 3. Perform a quick connectivity check. ServiceClient 
            // initialization is lazy, so we must execute a request.
            await modern.ExecuteAsync(new WhoAmIRequest(), default);

            return modern;
         }
         catch
         {
            // 4. Fallback to legacy if modern fails
            var legacy = new LegacyCrmProvider(connectionString);
            await legacy.ExecuteAsync(new WhoAmIRequest(), default);

            return legacy;
         }
      }

      /// <summary>
      /// Creates a provider using a forced legacy flag.
      /// </summary>
      public static async Task<IDataverseProvider> CreateAsync(
         string connectionString,
         bool isLegacy
      )
      {
         IDataverseProvider provider = isLegacy
            ? new LegacyCrmProvider(connectionString)
            : new DataverseProvider(connectionString);

         await provider.ExecuteAsync(new WhoAmIRequest(), default);

         return provider;
      }

      private static bool IsObviousLegacy(string connectionString)
      {
         // 1. Check Auth Markers
         var legacyMarkers = new[]
         {
            "AuthType=AD",
            "AuthType=IFD",
            "AuthType=Claims"
         };

         if (legacyMarkers.Any(m =>
            connectionString.Contains(m, StringComparison.OrdinalIgnoreCase)))
         {
            return true;
         }

         // 2. Check URL Pattern
         // If it contains a cloud domain, it's likely NOT legacy
         var cloudDomains = new[]
         {
            ".dynamics.com",
            ".powerapps.com",
            ".microsoftcrmportals.com"
         };

         var hasCloudDomain = cloudDomains.Any(d =>
            connectionString.Contains(d, StringComparison.OrdinalIgnoreCase));

         // If it has NO cloud domain but has a URL/ServiceUri,
         // it's likely OnPrem
         if (!hasCloudDomain && 
             (connectionString.Contains(
                "Url=", 
                StringComparison.OrdinalIgnoreCase) || 
              connectionString.Contains(
                "ServiceUri=", 
                StringComparison.OrdinalIgnoreCase)))
         {
            return true;
         }

         return false;
      }
   }
}
