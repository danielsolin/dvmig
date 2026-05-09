using System.Reflection;
using System.Text.Json;

namespace dvmig.Core.Shared
{
   /// <summary>
   /// Provides localization services by loading translations from embedded 
   /// JSON resources.
   /// </summary>
   public static class LocalizationService
   {
      private static Dictionary<string, string> _translations = new();
      private static string _currentLanguage = "en";

      /// <summary>
      /// Initializes the localization service for the specified language.
      /// </summary>
      /// <param name="languageCode">The language code (e.g., "en", "sv").
      /// </param>
      public static void Initialize(string languageCode)
      {
         _currentLanguage = languageCode?.ToLowerInvariant() ?? "en";

         if (_currentLanguage == "en")
         {
            _translations.Clear();
            return;
         }

         LoadTranslations();
      }

      /// <summary>
      /// Translates the specified text.
      /// </summary>
      /// <param name="text">The text to translate.</param>
      /// <returns>The translated text, or the original text if no translation 
      /// is found.</returns>
      public static string Translate(string text)
      {
         if (string.IsNullOrEmpty(text) || _currentLanguage == "en")
            return text;

         return _translations.TryGetValue(text, out var translated) 
            ? translated 
            : text;
      }

      private static void LoadTranslations()
      {
         var assembly = Assembly.GetExecutingAssembly();
         var resourceName = $"dvmig.Core.Resources.lang-{_currentLanguage}.json";

         using var stream = assembly.GetManifestResourceStream(resourceName);
         if (stream == null)
            return;

         try
         {
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            _translations = JsonSerializer.Deserialize<Dictionary<string, string>>(
               json) ?? new Dictionary<string, string>();
         }
         catch
         {
            // Fallback to empty dictionary on error
            _translations = new Dictionary<string, string>();
         }
      }
   }
}
