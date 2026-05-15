using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using dvmig.Core.Interfaces;
using dvmig.Core.Shared;

namespace dvmig.Core.Settings
{
   /// <summary>
   /// Implementation of the settings service using local file storage and 
   /// DPAPI for encryption (on Windows).
   /// </summary>
   public class SettingsService : ISettingsService
   {
      private readonly string _filePath;

      private static readonly byte[] LegacyEntropy =
         Encoding.UTF8.GetBytes("dvmig-entropy");

      /// <summary>
      /// Initializes a new instance of the 
      /// <see cref="SettingsService"/> class.
      /// </summary>
      public SettingsService()
      {
         string folder;

         bool isWindows =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

         if(isWindows)
         {
            folder = Path.Combine(
               Environment.GetFolderPath(
                  Environment.SpecialFolder.ApplicationData
               ),
               SystemConstants.AppConstants.AppName
            );
         }
         else
         {
            // Use hidden folder in home directory for Linux/macOS
            folder = Path.Combine(
               Environment.GetFolderPath(
                  Environment.SpecialFolder.UserProfile
               ),
               "." + SystemConstants.AppConstants.AppName
            );
         }

         if(!Directory.Exists(folder))
            Directory.CreateDirectory(folder);

         _filePath = Path.Combine(
            folder,
            SystemConstants.AppConstants.SettingsFileName
         );
      }

      /// <inheritdoc />
      public UserSettings LoadSettings()
      {
         if(!File.Exists(_filePath))
            return new UserSettings();

         try
         {
            var json = File.ReadAllText(_filePath);
            var settings = JsonSerializer.Deserialize<UserSettings>(json) ??
               new UserSettings();

            settings.SourceConnectionString =
               Decrypt(settings.SourceConnectionString);

            settings.TargetConnectionString =
               Decrypt(settings.TargetConnectionString);

            return settings;
         }
         catch
         {
            return new UserSettings();
         }
      }

      /// <inheritdoc />
      public void SaveSettings(UserSettings settings)
      {
         try
         {
            var settingsCopy = new UserSettings
            {
               Language = settings.Language,
               MaxParallelism = settings.MaxParallelism,
               AutoCreateRelatedRecords = settings.AutoCreateRelatedRecords,
               SourceIsLegacy = settings.SourceIsLegacy,
               TargetIsLegacy = settings.TargetIsLegacy
            };

            settingsCopy.SourceConnectionString =
               Encrypt(settings.SourceConnectionString);

            settingsCopy.TargetConnectionString =
               Encrypt(settings.TargetConnectionString);

            var json = JsonSerializer.Serialize(settingsCopy);

            File.WriteAllText(_filePath, json);
         }
         catch
         {
            // Log error in real app
         }
      }

      /// <summary>
      /// Encrypts the specified text.
      /// </summary>
      private string Encrypt(string text)
      {
         if(string.IsNullOrEmpty(text))
            return string.Empty;

         bool isWindows =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

         // DPAPI is only available on Windows
         if(!isWindows)
            return text;

         try
         {
            var data = Encoding.UTF8.GetBytes(text);
            var encrypted = ProtectedData.Protect(
               data,
               LegacyEntropy,
               DataProtectionScope.CurrentUser
            );

            return Convert.ToBase64String(encrypted);
         }
         catch
         {
            return text;
         }
      }

      /// <summary>
      /// Decrypts the specified base64 string.
      /// </summary>
      private string Decrypt(string base64)
      {
         if(string.IsNullOrEmpty(base64))
            return string.Empty;

         bool isWindows =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

         // DPAPI is only available on Windows
         if(!isWindows)
            return base64;

         try
         {
            var bytes = Convert.FromBase64String(base64);

            try
            {
               // Try mandated decryption with legacy entropy
               var decrypted = ProtectedData.Unprotect(
                  bytes,
                  LegacyEntropy,
                  DataProtectionScope.CurrentUser
               );

               return Encoding.UTF8.GetString(decrypted);
            }
            catch(CryptographicException)
            {
               // Fallback: try Jules' "no-entropy" format just in case
               var decrypted = ProtectedData.Unprotect(
                  bytes,
                  null,
                  DataProtectionScope.CurrentUser
               );

               return Encoding.UTF8.GetString(decrypted);
            }
         }
         catch
         {
            return base64;
         }
      }
   }
}

