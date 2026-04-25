using System.Security.Cryptography;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;

namespace dvmig.Core.Settings
{
    /// <summary>
    /// Represents the persisted user settings for the application.
    /// </summary>
    public class UserSettings
    {
        /// <summary>
        /// Gets or sets the encrypted connection string for the source 
        /// environment.
        /// </summary>
        public string SourceConnectionString { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the encrypted connection string for the target 
        /// environment.
        /// </summary>
        public string TargetConnectionString { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a value indicating whether connection strings 
        /// should be persisted.
        /// </summary>
        public bool RememberConnections { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to automatically attempt 
        /// connection on startup.
        /// </summary>
        public bool AutoConnect { get; set; }
    }

    /// <summary>
    /// Service interface for loading and saving application settings.
    /// </summary>
    public interface ISettingsService
    {
        /// <summary>
        /// Loads the user settings from persistent storage.
        /// </summary>
        /// <returns>The loaded user settings.</returns>
        UserSettings LoadSettings();

        /// <summary>
        /// Saves the specified user settings to persistent storage.
        /// </summary>
        /// <param name="settings">The settings to save.</param>
        void SaveSettings(UserSettings settings);
    }

    /// <summary>
    /// Implementation of the settings service using local file storage and 
    /// DPAPI for encryption.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class SettingsService : ISettingsService
    {
        private readonly string _filePath;

        private static readonly byte[] LegacyEntropy =
            Encoding.UTF8.GetBytes("dvmig-entropy");

        /// <summary>
        /// Initializes a new instance of the <see cref="SettingsService"/> class.
        /// </summary>
        public SettingsService()
        {
            var appData = Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData
            );

            var folder = Path.Combine(appData, "dvmig");

            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            _filePath = Path.Combine(folder, "settings.json");
        }

        /// <inheritdoc />
        public UserSettings LoadSettings()
        {
            if (!File.Exists(_filePath))
            {
                return new UserSettings();
            }

            try
            {
                var json = File.ReadAllText(_filePath);
                var settings = JsonSerializer.Deserialize<UserSettings>(json) ??
                               new UserSettings();

                if (settings.RememberConnections)
                {
                    settings.SourceConnectionString =
                        Decrypt(settings.SourceConnectionString);

                    settings.TargetConnectionString =
                        Decrypt(settings.TargetConnectionString);
                }
                else
                {
                    settings.SourceConnectionString = string.Empty;
                    settings.TargetConnectionString = string.Empty;
                }

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
                    RememberConnections = settings.RememberConnections,
                    AutoConnect = settings.AutoConnect
                };

                if (settings.RememberConnections)
                {
                    settingsCopy.SourceConnectionString =
                        Encrypt(settings.SourceConnectionString);

                    settingsCopy.TargetConnectionString =
                        Encrypt(settings.TargetConnectionString);
                }

                var json = JsonSerializer.Serialize(settingsCopy);
                File.WriteAllText(_filePath, json);
            }
            catch
            {
                // Log error in real app
            }
        }

        /// <summary>
        /// Encrypts the specified text using DPAPI.
        /// </summary>
        private string Encrypt(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

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
                return string.Empty;
            }
        }

        /// <summary>
        /// Decrypts the specified base64 string using DPAPI.
        /// </summary>
        private string Decrypt(string base64)
        {
            if (string.IsNullOrEmpty(base64))
            {
                return string.Empty;
            }

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
                catch (CryptographicException)
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
                return string.Empty;
            }
        }
    }
}
