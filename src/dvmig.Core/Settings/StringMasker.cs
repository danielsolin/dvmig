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

         foreach (var part in parts)
         {
            var kv = part.Split(new[] { '=' }, 2);

            if (kv.Length != 2)
               continue;

            var key = kv[0].Trim();
            var val = kv[1].Trim();

            var comp = StringComparison.OrdinalIgnoreCase;

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

         return Connection.UnknownEnvironment;
      }
   }
}
