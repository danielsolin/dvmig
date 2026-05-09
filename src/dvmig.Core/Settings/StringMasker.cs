using dvmig.Core.Shared;

using static dvmig.Core.Shared.SystemConstants;

namespace dvmig.Core.Settings
{
   /// <summary>
   /// Provides helper methods for manipulating Dataverse connection strings.
   /// </summary>
   public static class StringMasker
   {
      /// <summary>
      /// Masks sensitive information (passwords, secrets) in a Dataverse 
      /// connection string for secure display.
      /// </summary>
      /// <param name="connectionString">
      /// The raw connection string to mask.
      /// </param>
      /// <returns>A masked version of the connection string.</returns>
      public static string MaskConnectionString(string connectionString)
      {
         if (string.IsNullOrEmpty(connectionString))
            return string.Empty;

         var parts = connectionString.Split(
            new[] { ';' },
            StringSplitOptions.RemoveEmptyEntries
         );

         var maskedParts = new List<string>();

         foreach (var part in parts)
         {
            var kv = part.Split(new[] { '=' }, 2);

            if (kv.Length != 2)
            {
               maskedParts.Add(part);

               continue;
            }

            var key = kv[0].Trim();
            var val = kv[1].Trim();

            if (IsSensitive(key))
               maskedParts.Add($"{key}=********");
            else
               maskedParts.Add($"{key}={val}");
         }

         return string.Join(";", maskedParts);
      }

      /// <summary>
      /// Extracts only the environment URL from a connection string or 
      /// returns the string itself if it's already a URL.
      /// </summary>
      public static string GetEnvironmentUrl(string connectionString)
      {
         if (string.IsNullOrWhiteSpace(connectionString))
            return "(Not Set)".t();

         var comp = StringComparison.OrdinalIgnoreCase;

         // Check if it's already a raw URL
         if ((connectionString.StartsWith(Connection.Https, comp) ||
              connectionString.StartsWith(Connection.Http, comp)) &&
             !connectionString.Contains(";"))
         {
            var url = connectionString;

            if (url.StartsWith(Connection.Https, comp))
               url = url.Substring(8);
            else if (url.StartsWith(Connection.Http, comp))
               url = url.Substring(7);

            return url.TrimEnd('/');
         }

         var parts = connectionString.Split(
            new[] { ';' },
            StringSplitOptions.RemoveEmptyEntries
         );

         foreach (var part in parts)
         {
            var kv = part.Split(new[] { '=' }, 2);

            if (kv.Length != 2)
               continue;

            var key = kv[0].Trim();
            var val = kv[1].Trim();

            if (string.Equals(key, Connection.Url, comp) ||
                string.Equals(key, Connection.ServiceUri, comp) ||
                string.Equals(key, Connection.Server, comp))
            {
               var url = val;

               if (url.StartsWith(Connection.Https, comp))
                  url = url.Substring(8);
               else if (url.StartsWith(Connection.Http, comp))
                  url = url.Substring(7);

               return url.TrimEnd('/');
            }
         }

         return connectionString.Length > 40
            ? connectionString.Substring(0, 37) + "..."
            : connectionString;
      }

      private static bool IsSensitive(string key)
      {
         var comp = StringComparison.OrdinalIgnoreCase;

         return 
            key.IndexOf(MaskingKeywords.Password, comp) >= 0 ||
            key.IndexOf(MaskingKeywords.Secret, comp) >= 0 ||
            key.IndexOf(MaskingKeywords.Token, comp) >= 0 ||
            key.IndexOf(MaskingKeywords.Thumbprint, comp) >= 0;
      }
   }
}
