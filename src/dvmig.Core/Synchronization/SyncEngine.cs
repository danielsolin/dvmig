using dvmig.Core.Interfaces;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

using CT = System.Threading.CancellationToken;

namespace dvmig.Core.Synchronization
{
   /// <summary>
   /// Core orchestrator responsible for initializing the sync and pulling
   /// entity pages from the source to the target Dataverse environment.
   /// </summary>
   public partial class SyncEngine : ISyncEngine
   {
      private readonly IDataverseProvider _source;
      private readonly IDataverseProvider _target;
      private readonly ILogger _logger;
      private readonly IEntityService _entityService;
      private readonly IMetadataService _metadataService;
      private readonly ISyncStateService _syncStateService;
      private readonly ISyncRecordService _syncRecordService;

      /// <summary>
      /// Initializes a new instance of the <see cref="SyncEngine"/> class.
      /// </summary>
      /// <param name="source">The source Dataverse provider.</param>
      /// <param name="target">The target Dataverse provider.</param>
      /// <param name="logger">The logger instance.</param>
      /// <param name="entityService">The entity service.</param>
      /// <param name="metadataService">The metadata service.</param>
      /// <param name="syncStateService">The sync state service.</param>
      /// <param name="syncRecordService">The record sync service.</param>
      public SyncEngine(
         IDataverseProvider source,
         IDataverseProvider target,
         ILogger logger,
         IEntityService entityService,
         IMetadataService metadataService,
         ISyncStateService syncStateService,
         ISyncRecordService syncRecordService
      )
      {
         _source = source;
         _target = target;
         _logger = logger;
         _entityService = entityService;
         _metadataService = metadataService;
         _syncStateService = syncStateService;
         _syncRecordService = syncRecordService;
      }

      /// <inheritdoc />
      public async Task InitializeEntitySyncAsync(
         string logicalName,
         CT ct = default
      )
      {
         var ids = await _entityService.GetAllIdsAsync(
            _target,
            logicalName,
            ct
         );

         _syncStateService.InitializeSyncedIds(ids);
      }

      /// <inheritdoc />
      public async Task SyncAsync(
         string logicalName,
         SyncOptions options,
         QueryExpression? query = null,
         IProgress<bool>? recordProgress = null,
         CT ct = default
      )
      {
         await InitializeEntitySyncAsync(logicalName, ct);

         var columns = await _metadataService.GetValidColumnsAsync(
            logicalName,
            ct
         );

         var syncQuery = query ?? new QueryExpression(logicalName)
         {
            ColumnSet = columns
         };
         syncQuery.PageInfo = new PagingInfo
         {
            Count = 500,
            PageNumber = 1
         };

         int totalSynced = 0;

         while (true)
         {
            var response = await _source.RetrieveMultipleAsync(
               syncQuery,
               ct
            );

            if (response.Entities.Count == 0)
               break;

            var count = response.Entities.Count;
            var recordCountSoFar = totalSynced + count;

            _logger.Information(
               $"Syncing {logicalName} page " +
               $"{syncQuery.PageInfo.PageNumber} " +
               $"({recordCountSoFar} records " +
               $"found so far)..."
            );

            await ProcessBatchAsync(
               response.Entities,
               options,
               recordProgress,
               ct
            );

            totalSynced += response.Entities.Count;

            if (!response.MoreRecords)
               break;

            syncQuery.PageInfo.PageNumber++;
            syncQuery.PageInfo.PagingCookie = response.PagingCookie;
         }

         _logger.Information(
            "SyncEntity {Entity} finished. Total records: {Count}",
            logicalName,
            totalSynced
         );

         _syncStateService.TriedDependencies.Clear();
         _syncStateService.IdMappingCache.Clear();
      }

      private async Task ProcessBatchAsync(
         IEnumerable<Entity> entities,
         SyncOptions options,
         IProgress<bool>? recordProgress = null,
         CT ct = default
      )
      {
         var entitiesToSync = entities
            .Where(e => !_syncStateService.IsSynced(e.Id))
            .ToList();

         if (!entitiesToSync.Any())
            return;

         var parallelOptions = new ParallelOptions
         {
            MaxDegreeOfParallelism = options.MaxDegreeOfParallelism,
            CancellationToken = ct
         };

         await Parallel.ForEachAsync(
            entitiesToSync,
            parallelOptions,
            async (entity, token) =>
               await _syncRecordService.SyncRecordAndReportAsync(
                  entity,
                  options,
                  recordProgress,
                  token
               )
         );
      }
   }
}

