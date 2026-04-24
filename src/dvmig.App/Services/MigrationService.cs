using dvmig.App.Models;
using dvmig.Providers;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using dvmig.Core.Metadata;

namespace dvmig.App.Services
{
    public interface IMigrationService
    {
        Task<bool> ConnectSourceAsync(
            string connectionString,
            bool isLegacy,
            CancellationToken ct = default
        );

        Task<bool> ConnectTargetAsync(
            string connectionString,
            bool isLegacy,
            CancellationToken ct = default
        );

        Task<List<EntityMetadata>> GetSourceEntitiesAsync(
            CancellationToken ct = default
        );

        Task<long> GetRecordCountAsync(
            string logicalName,
            CancellationToken ct = default
        );

        Task<List<RecordSelectionItem>> GetRecordsAsync(
            string logicalName,
            string? searchText = null,
            CancellationToken ct = default
        );

        void DisconnectSource();

        void DisconnectTarget();

        IDataverseProvider? SourceProvider { get; }
        IDataverseProvider? TargetProvider { get; }

        List<EntitySyncConfiguration> SelectedEntities { get; }

        bool IsStandardEntity(string logicalName);
    }

    public class MigrationService : IMigrationService
    {
        private List<EntityMetadata>? _cachedMetadata;

        public IDataverseProvider? SourceProvider { get; private set; }

        public IDataverseProvider? TargetProvider { get; private set; }

        public List<EntitySyncConfiguration> SelectedEntities { get; } = 
            new List<EntitySyncConfiguration>();

        public void DisconnectSource()
        {
            SourceProvider = null;
            _cachedMetadata = null;
        }

        public void DisconnectTarget()
        {
            TargetProvider = null;
        }

        public async Task<bool> ConnectSourceAsync(
            string connectionString,
            bool isLegacy,
            CancellationToken ct = default)
        {
            try
            {
                SourceProvider = isLegacy
                    ? await Task.Run(
                        () =>
                        {
                            ct.ThrowIfCancellationRequested();

                            return new LegacyCrmProvider(connectionString);
                        },
                        ct
                    )
                    : await Task.Run(
                        () =>
                        {
                            ct.ThrowIfCancellationRequested();

                            return new DataverseProvider(connectionString);
                        },
                        ct
                    );

                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> ConnectTargetAsync(
            string connectionString,
            bool isLegacy,
            CancellationToken ct = default)
        {
            try
            {
                TargetProvider = isLegacy
                    ? await Task.Run(
                        () =>
                        {
                            ct.ThrowIfCancellationRequested();

                            return new LegacyCrmProvider(connectionString);
                        },
                        ct
                    )
                    : await Task.Run(
                        () =>
                        {
                            ct.ThrowIfCancellationRequested();

                            return new DataverseProvider(connectionString);
                        },
                        ct
                    );

                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
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

            if (_cachedMetadata != null)
            {
                return _cachedMetadata;
            }

            var request = new RetrieveAllEntitiesRequest
            {
                EntityFilters = EntityFilters.Entity,
                RetrieveAsIfPublished = true
            };

            var response = (RetrieveAllEntitiesResponse)await SourceProvider
                .ExecuteAsync(request, ct);

            _cachedMetadata = response.EntityMetadata
                .OrderBy(e =>
                    e.DisplayName?.UserLocalizedLabel?.Label ??
                    e.LogicalName
                )
                .ToList();

            return _cachedMetadata;
        }

        public async Task<long> GetRecordCountAsync(
            string logicalName,
            CancellationToken ct = default)
        {
            if (SourceProvider == null)
            {
                return 0;
            }

            var metadata = await GetSourceEntitiesAsync(ct);
            var entityMeta = metadata.FirstOrDefault(e => 
                e.LogicalName == logicalName
            );

            if (entityMeta == null)
            {
                return 0;
            }

            var primaryId = entityMeta.PrimaryIdAttribute;

            var fetchXml = $@"
                <fetch aggregate='true'>
                  <entity name='{logicalName}'>
                    <attribute name='{primaryId}' alias='count' aggregate='count' />
                  </entity>
                </fetch>";

            var response = await SourceProvider.RetrieveMultipleAsync(
                new FetchExpression(fetchXml),
                ct
            );

            if (response.Entities.Count > 0 && 
                response.Entities[0].Contains("count"))
            {
                var aliasedValue = (AliasedValue)response.Entities[0]["count"];
                return (int)aliasedValue.Value;
            }

            return 0;
        }

        public async Task<List<RecordSelectionItem>> GetRecordsAsync(
            string logicalName,
            string? searchText = null,
            CancellationToken ct = default)
        {
            if (SourceProvider == null)
            {
                return new List<RecordSelectionItem>();
            }

            var metadata = await GetSourceEntitiesAsync(ct);
            var entityMeta = metadata.FirstOrDefault(e => 
                e.LogicalName == logicalName
            );

            if (entityMeta == null)
            {
                return new List<RecordSelectionItem>();
            }

            var primaryId = entityMeta.PrimaryIdAttribute;
            var primaryName = entityMeta.PrimaryNameAttribute;

            var query = new QueryExpression(logicalName)
            {
                ColumnSet = new ColumnSet(primaryId, primaryName),
                TopCount = 100
            };

            if (!string.IsNullOrWhiteSpace(searchText))
            {
                query.Criteria.AddCondition(
                    primaryName, 
                    ConditionOperator.Like, 
                    $"%{searchText}%"
                );
            }

            var results = await SourceProvider.RetrieveMultipleAsync(
                query, 
                ct
            );

            var config = SelectedEntities.FirstOrDefault(c => 
                c.LogicalName == logicalName
            );

            return results.Entities.Select(e => new RecordSelectionItem(
                e.Id,
                e.Contains(primaryName) ? e[primaryName].ToString()! : e.Id.ToString(),
                config?.SelectedRecordIds.Contains(e.Id) ?? false
            )).ToList();
        }

        public bool IsStandardEntity(string logicalName)
        {
            return EntityMetadataHelper.IsStandardEntity(logicalName);
        }
    }
}
