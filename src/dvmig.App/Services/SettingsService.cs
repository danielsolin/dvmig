using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace dvmig.App.Services
{
    public class UserSettings
    {
        public string SourceConnectionString { get; set; } = string.Empty;

        public string TargetConnectionString { get; set; } = string.Empty;

        public bool RememberConnections { get; set; }

        public bool AutoConnect { get; set; }
    }

    public interface ISettingsService
    {
        UserSettings LoadSettings();

        void SaveSettings(UserSettings settings);
    }

    public class SettingsService : ISettingsService
    {
        private readonly string _filePath;

        private static readonly byte[] LegacyEntropy =
            Encoding.UTF8.GetBytes("dvmig-entropy");

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
