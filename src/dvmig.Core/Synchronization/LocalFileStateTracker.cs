using System.Security.Cryptography;
using System.Text;
using dvmig.Core.Interfaces;

namespace dvmig.Core.Synchronization
{
    /// <summary>
    /// Implements sync state tracking using local files in the AppData directory.
    /// </summary>
    public class LocalFileStateTracker : ISyncStateTracker
    {
        private string? _filePath;
        private readonly SemaphoreSlim _fileLock = new SemaphoreSlim(1, 1);

        /// <inheritdoc />
        public Task InitializeAsync(
            string sourceKey, string targetKey, string logicalName)
        {
            var appData = Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData
            );

            var normalizedKey = $"{NormalizeConnectionString(sourceKey)}"
                                + "|{NormalizeConnectionString(targetKey)}";
            var normalizedHash = GetHash(normalizedKey);
            var normalizedFolder = Path.Combine(
                appData,
                "dvmig",
                "state",
                normalizedHash
            );
            var normalizedPath = Path.Combine(
                normalizedFolder,
                $"{logicalName}.txt"
            );

            // Fallback to the old raw-hash path if state already exists there.
            var rawKey = $"{sourceKey}|{targetKey}";
            var rawHash = GetHash(rawKey);
            var rawFolder = Path.Combine(appData, "dvmig", "state", rawHash);
            var rawPath = Path.Combine(rawFolder, $"{logicalName}.txt");

            if (File.Exists(rawPath))
            {
                _filePath = rawPath;
                return Task.CompletedTask;
            }

            if (!Directory.Exists(normalizedFolder))
            {
                Directory.CreateDirectory(normalizedFolder);
            }

            _filePath = normalizedPath;

            return Task.CompletedTask;
        }

        private static string NormalizeConnectionString(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return string.Empty;
            }

            var parts = connectionString.Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries
            );

            var normalizedPairs = new List<KeyValuePair<string, string>>();
            foreach (var part in parts)
            {
                var kv = part.Split('=', 2);
                if (kv.Length != 2)
                {
                    continue;
                }

                var key = kv[0].Trim().ToLowerInvariant();
                var value = kv[1].Trim();

                if (string.IsNullOrEmpty(key) ||
                    IsSensitiveConnectionKey(key))
                {
                    continue;
                }

                normalizedPairs.Add(
                    new KeyValuePair<string, string>(key, value)
                );
            }

            normalizedPairs.Sort((a, b) =>
                string.CompareOrdinal(a.Key, b.Key));

            var sb = new StringBuilder();
            foreach (var pair in normalizedPairs)
            {
                sb.Append(pair.Key);
                sb.Append('=');
                sb.Append(pair.Value);
                sb.Append(';');
            }

            return sb.ToString();
        }

        private static bool IsSensitiveConnectionKey(string key)
        {
            return
                key.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("token", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("thumbprint", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("clientid", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("appid", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("userid", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("user id", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("username", StringComparison.OrdinalIgnoreCase);
        }

        /// <inheritdoc />
        public bool StateExists()
        {
            return !string.IsNullOrEmpty(_filePath) && File.Exists(_filePath);
        }

        /// <inheritdoc />
        public async Task<HashSet<Guid>> GetSyncedIdsAsync()
        {
            if (!StateExists())
            {
                return new HashSet<Guid>();
            }

            await _fileLock.WaitAsync();
            try
            {
                var lines = await File.ReadAllLinesAsync(_filePath!);
                var ids = new HashSet<Guid>();

                foreach (var line in lines)
                {
                    if (Guid.TryParse(line, out var id))
                    {
                        ids.Add(id);
                    }
                }

                return ids;
            }
            finally
            {
                _fileLock.Release();
            }
        }

        /// <inheritdoc />
        public async Task MarkAsSyncedAsync(Guid id)
        {
            if (string.IsNullOrEmpty(_filePath))
            {
                throw new InvalidOperationException("Tracker not initialized.");
            }

            await _fileLock.WaitAsync();
            try
            {
                await File.AppendAllLinesAsync(
                    _filePath,
                    new[] { id.ToString() }
                );
            }
            finally
            {
                _fileLock.Release();
            }
        }

        /// <inheritdoc />
        public async Task ClearStateAsync()
        {
            if (StateExists())
            {
                await _fileLock.WaitAsync();
                try
                {
                    File.Delete(_filePath!);
                }
                finally
                {
                    _fileLock.Release();
                }
            }
        }

        private string GetHash(string input)
        {
            var bytes = Encoding.UTF8.GetBytes(input);
            var hashBytes = SHA256.HashData(bytes);

            var sb = new StringBuilder();
            foreach (var b in hashBytes)
            {
                sb.Append(b.ToString("x2"));
            }

            return sb.ToString().Substring(0, 16); // Short hash is enough
        }
    }
}
