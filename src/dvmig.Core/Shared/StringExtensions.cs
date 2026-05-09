namespace dvmig.Core.Shared
{
   /// <summary>
   /// Provides extension methods for string localization.
   /// </summary>
   public static class StringExtensions
   {
      /// <summary>
      /// Translates the string using the LocalizationService.
      /// </summary>
      /// <param name="text">The text to translate.</param>
      /// <param name="args">Optional arguments for string formatting.</param>
      /// <returns>The translated (and optionally formatted) string.</returns>
      public static string t(this string text, params object[] args)
      {
         var translated = LocalizationService.Translate(text);

         if (args == null || args.Length == 0)
            return translated;

         try
         {
            return string.Format(translated, args);
         }
         catch
         {
            // If formatting fails, return the translated (or original) 
            // string as is
            return translated;
         }
      }
   }
}
