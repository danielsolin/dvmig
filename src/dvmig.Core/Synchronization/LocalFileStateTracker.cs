using System.Security.Cryptography;
using System.Text;

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
        public Task InitializeAsync(string sourceKey, string targetKey, string logicalName)
        {
            var appData = Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData
            );

            // Create a unique hash for the source/target pair
            var combinedKey = $"{sourceKey}|{targetKey}";
            var hash = GetHash(combinedKey);

            var folder = Path.Combine(appData, "dvmig", "state", hash);
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            _filePath = Path.Combine(folder, $"{logicalName}.txt");
            return Task.CompletedTask;
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
