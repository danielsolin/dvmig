using dvmig.Providers;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;

namespace dvmig.App.Services
{
    public interface IMigrationService
    {
        Task<bool> ConnectSourceAsync(string connectionString, bool isLegacy);
        Task<bool> ConnectTargetAsync(string connectionString, bool isLegacy);

        Task<List<EntityMetadata>> GetSourceEntitiesAsync(CancellationToken ct = default);

        IDataverseProvider? SourceProvider { get; }
        IDataverseProvider? TargetProvider { get; }

        List<string> SelectedEntities { get; }
    }

    public class MigrationService : IMigrationService
    {
        public IDataverseProvider? SourceProvider { get; private set; }
        public IDataverseProvider? TargetProvider { get; private set; }
        public List<string> SelectedEntities { get; } = new List<string>();

        public async Task<bool> ConnectSourceAsync(string connectionString, bool isLegacy)
        {
            try
            {
                SourceProvider = isLegacy
                    ? await Task.Run(() => new LegacyCrmProvider(connectionString))
                    : await Task.Run(() => new DataverseProvider(connectionString));

                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> ConnectTargetAsync(string connectionString, bool isLegacy)
        {
            try
            {
                TargetProvider = isLegacy
                    ? await Task.Run(() => new LegacyCrmProvider(connectionString))
                    : await Task.Run(() => new DataverseProvider(connectionString));

                return true;
            }
            catch
            {
                return false;
            }
        }

        public Task<bool> TestConnectionAsync(
            string connectionString,
            bool isLegacy)
        {
            // Deprecated, using ConnectSource/ConnectTarget
            return Task.FromResult(false);
        }

        public async Task<List<EntityMetadata>> GetSourceEntitiesAsync(
            CancellationToken ct = default)
        {
            if (SourceProvider == null)
            {
                return new List<EntityMetadata>();
            }

            var request = new RetrieveAllEntitiesRequest
            {
                EntityFilters = EntityFilters.Entity,
                RetrieveAsIfPublished = true
            };

            var response = (RetrieveAllEntitiesResponse)await SourceProvider
                .ExecuteAsync(request, ct);

            return response.EntityMetadata
                .Where(e => e.IsCustomEntity == true || IsStandardEntity(e.LogicalName))
                .OrderBy(e => e.DisplayName?.UserLocalizedLabel?.Label ?? e.LogicalName)
                .ToList();
        }

        private bool IsStandardEntity(string logicalName)
        {
            var standard = new[] { "account", "contact", "lead", "opportunity", "task" };

            return standard.Contains(logicalName.ToLower());
        }
    }
}
