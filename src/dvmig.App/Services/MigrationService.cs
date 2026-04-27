using dvmig.App.Models;
using dvmig.Core.Interfaces;
using dvmig.Core.Shared;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace dvmig.App.Services
{
    /// <summary>
    /// Service interface for managing Dataverse connections and retrieving 
    /// metadata and records for migration selection.
    /// </summary>
    public interface IMigrationService
    {
        /// <summary>
        /// Asynchronously connects to the source Dataverse/CRM environment.
        /// </summary>
        /// <param name="connectionString">The connection string to use.</param>
        /// <param name="isLegacy">
        /// True if using legacy CRM OnPrem authentication (AD/IFD).
        /// </param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>True if connection was successful; otherwise, false.</returns>
        Task<bool> ConnectSourceAsync(
            string connectionString,
            bool isLegacy,
            CancellationToken ct = default
        );

        /// <summary>
        /// Asynchronously connects to the target Dataverse environment.
        /// </summary>
        /// <param name="connectionString">The connection string to use.</param>
        /// <param name="isLegacy">
        /// True if using legacy CRM OnPrem authentication.
        /// </param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>True if connection was successful; otherwise, false.</returns>
        Task<bool> ConnectTargetAsync(
            string connectionString,
            bool isLegacy,
            CancellationToken ct = default
        );

        /// <summary>
        /// Retrieves all entity metadata from the source environment.
        /// </summary>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>A list of entity metadata.</returns>
        Task<List<EntityMetadata>> GetSourceEntitiesAsync(
            CancellationToken ct = default
        );

        /// <summary>
        /// Gets the total record count for a specific entity in the 
        /// source environment.
        /// </summary>
        /// <param name="logicalName">The logical name of the entity.</param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>The total number of records.</returns>
        Task<long> GetRecordCountAsync(
            string logicalName,
            CancellationToken ct = default
        );

        /// <summary>
        /// Retrieves a sample list of records for a specific entity from 
        /// the source environment.
        /// </summary>
        /// <param name="logicalName">The logical name of the entity.</param>
        /// <param name="searchText">
        /// Optional search text to filter records by their primary name.
        /// </param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>A list of record selection items.</returns>
        Task<List<RecordSelectionItem>> GetRecordsAsync(
            string logicalName,
            string? searchText = null,
            CancellationToken ct = default
        );

        /// <summary>
        /// Disconnects the source environment provider and clears 
        /// cached metadata.
        /// </summary>
        void DisconnectSource();

        /// <summary>
        /// Disconnects the target environment provider.
        /// </summary>
        void DisconnectTarget();

        /// <summary>
        /// Gets the current source Dataverse provider.
        /// </summary>
        IDataverseProvider? SourceProvider { get; }

        /// <summary>
        /// Gets the current target Dataverse provider.
        /// </summary>
        IDataverseProvider? TargetProvider { get; }

        /// <summary>
        /// Gets the list of currently configured entity migrations.
        /// </summary>
        List<EntitySyncConfiguration> SelectedEntities { get; }

        /// <summary>
        /// Determines if the specified entity is considered a standard 
        /// (non-system) entity.
        /// </summary>
        /// <param name="logicalName">The logical name of the entity.</param>
        /// <returns>True if it is a standard entity; otherwise, false.</returns>
        bool IsStandardEntity(string logicalName);
    }

    /// <summary>
    /// Implementation of the migration service for managing Dataverse 
    /// operations within the application.
    /// </summary>
    public class MigrationService : IMigrationService
    {
        private List<EntityMetadata>? _cachedMetadata;

        /// <inheritdoc />
        public IDataverseProvider? SourceProvider { get; private set; }

        /// <inheritdoc />
        public IDataverseProvider? TargetProvider { get; private set; }

        /// <inheritdoc />
        public List<EntitySyncConfiguration> SelectedEntities { get; } =
            new List<EntitySyncConfiguration>();

        /// <inheritdoc />
        public void DisconnectSource()
        {
            SourceProvider = null;
            _cachedMetadata = null;
        }

        /// <inheritdoc />
        public void DisconnectTarget()
        {
            TargetProvider = null;
        }

        /// <inheritdoc />
        public async Task<bool> ConnectSourceAsync(
            string connectionString,
            bool isLegacy,
            CancellationToken ct = default)
        {
            SourceProvider = await ConnectProviderAsync(
                connectionString,
                isLegacy,
                ct
            );

            return SourceProvider != null;
        }

        /// <inheritdoc />
        public async Task<bool> ConnectTargetAsync(
            string connectionString,
            bool isLegacy,
            CancellationToken ct = default)
        {
            TargetProvider = await ConnectProviderAsync(
                connectionString,
                isLegacy,
                ct
            );

            return TargetProvider != null;
        }

        private async Task<IDataverseProvider?> ConnectProviderAsync(
            string connectionString,
            bool isLegacy,
            CancellationToken ct)
        {
            try
            {
                return await Task.Run(
                    () =>
                    {
                        ct.ThrowIfCancellationRequested();

                        IDataverseProvider provider;
                        if (isLegacy)
                        {
                            provider = new LegacyCrmProvider(connectionString);
                        }
                        else
                        {
                            provider = new DataverseProvider(connectionString);
                        }

                        return provider;
                    },
                    ct
                );
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch
            {
                return null;
            }
        }


        /// <inheritdoc />
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

        /// <inheritdoc />
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

        /// <inheritdoc />
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
                TopCount = SystemConstants.AppConstants.RecordFetchLimit
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

        /// <inheritdoc />
        public bool IsStandardEntity(string logicalName)
        {
            return EntityMetadataHelper.IsStandardEntity(logicalName);
        }
    }
}
