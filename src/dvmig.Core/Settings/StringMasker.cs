using dvmig.Core.Shared;

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
            ';',
            StringSplitOptions.RemoveEmptyEntries
         );

         var maskedParts = parts.Select(p =>
         {
            var kv = p.Split('=', 2);

            if (kv.Length != 2)
               return p;

            var key = kv[0].Trim();
            var val = kv[1].Trim();

            var comp = StringComparison.OrdinalIgnoreCase;

            bool isSensitive =
               key.Contains(SystemConstants.MaskingKeywords.Password, comp) ||
               key.Contains(SystemConstants.MaskingKeywords.Secret, comp) ||
               key.Contains(SystemConstants.MaskingKeywords.Token, comp);

            if (isSensitive)
               return $"{key}=********";

            return p;
         });

         return string.Join("; ", maskedParts);
      }
   }
}
