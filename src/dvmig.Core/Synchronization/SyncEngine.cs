using System.Collections.Concurrent;
using dvmig.Core.Interfaces;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Polly;
using Polly.Retry;
using Serilog;

namespace dvmig.Core.Synchronization
{
   /// <summary>
   /// Core engine responsible for synchronizing entities from the source
   /// to the target Dataverse environment. Handles dependency resolution,
   /// retries, and data transformations.
   /// </summary>
   public partial class SyncEngine : ISyncEngine
   {
      private readonly IDataverseProvider _source;
      private readonly IDataverseProvider _target;
      private readonly IUserMapper _userMapper;
      private readonly ISetupService _setupService;
      private readonly ISyncStateTracker _stateTracker;
      private readonly ILogger _logger;
      private readonly IRetryStrategy _retryStrategy;
      private readonly IEntityPreparer _entityPreparer;
      private readonly ISyncErrorHandler _errorHandler;
      private readonly IDependencyResolver _dependencyResolver;
      private readonly IStatusTransitionHandler _statusTransitionHandler;
      private readonly IMetadataCache _metadataCache;
      private readonly IFailureLogger _failureLogger;
      private readonly AsyncRetryPolicy _retryPolicy;

      private readonly ConcurrentDictionary<string, int> _recursionTracker =
          new ConcurrentDictionary<string, int>();

      private readonly ConcurrentDictionary<string, HashSet<string>>
          _triedDependencies =
              new ConcurrentDictionary<string, HashSet<string>>();

      private readonly ConcurrentDictionary<string, Guid> _idMappingCache =
          new ConcurrentDictionary<string, Guid>();

      private ConcurrentDictionary<Guid, byte> _syncedIds =
          new ConcurrentDictionary<Guid, byte>();

      private const int MaxRecursionDepth = 3;

      /// <summary>
      /// Initializes a new instance of the <see cref="SyncEngine"/> class.
      /// </summary>
      /// <param name="source">The source Dataverse provider.</param>
      /// <param name="target">The target Dataverse provider.</param>
      /// <param name="userMapper">The user mapping service.</param>
      /// <param name="setupService">The setup service.</param>
      /// <param name="stateTracker">The sync state tracking service.</param>
      /// <param name="logger">The logger instance.</param>
      /// <param name="retryStrategy">The retry strategy.</param>
      /// <param name="entityPreparer">The entity preparer.</param>
      /// <param name="errorHandler">The error handler.</param>
      /// <param name="dependencyResolver">The dependency resolver.</param>
      /// <param name="statusTransitionHandler">
      /// The status transition handler.
      /// </param>
      /// <param name="metadataCache">The metadata cache.</param>
      /// <param name="failureLogger">The failure logger.</param>
      public SyncEngine(
          IDataverseProvider source,
          IDataverseProvider target,
          IUserMapper userMapper,
          ISetupService setupService,
          ISyncStateTracker stateTracker,
          ILogger logger,
          IRetryStrategy retryStrategy,
          IEntityPreparer entityPreparer,
          ISyncErrorHandler errorHandler,
          IDependencyResolver dependencyResolver,
          IStatusTransitionHandler statusTransitionHandler,
          IMetadataCache metadataCache,
          IFailureLogger failureLogger
      )
      {
         _source = source;
         _target = target;
         _userMapper = userMapper;
         _setupService = setupService;
         _stateTracker = stateTracker;
         _logger = logger;
         _retryStrategy = retryStrategy;
         _entityPreparer = entityPreparer;
         _errorHandler = errorHandler;
         _dependencyResolver = dependencyResolver;
         _statusTransitionHandler = statusTransitionHandler;
         _metadataCache = metadataCache;
         _failureLogger = failureLogger;

         _retryPolicy = _retryStrategy.CreateRetryPolicy();
      }

      /// <inheritdoc />
      public async Task SyncEntityAsync(
         string logicalName,
         SyncOptions options,
         QueryExpression? query = null,
         IProgress<string>? progress = null,
         IProgress<bool>? recordProgress = null,
         CancellationToken ct = default
      )
      {
         await InitializeEntitySyncAsync(logicalName);

         var columns = await GetValidColumnsAsync(logicalName, ct);
         var syncQuery = query ?? new QueryExpression(logicalName)
         {
            ColumnSet = columns
         };

         // Ensure paging is initialized
         syncQuery.PageInfo = new Microsoft.Xrm.Sdk.Query.PagingInfo
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

            if (progress != null)
            {
               var count = response.Entities.Count;
               var recordCountSoFar = totalSynced + count;

               progress.Report(
                  $"Syncing {logicalName} page " +
                  $"{syncQuery.PageInfo.PageNumber} " +
                  $"({recordCountSoFar} records " +
                  $"found so far)..."
               );
            }

            await SyncAsync(
               response.Entities,
               options,
               progress,
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
      }

      /// <inheritdoc />
      public async Task InitializeEntitySyncAsync(string logicalName)
      {
         await _stateTracker.InitializeAsync(
            _source.ConnectionString,
            _target.ConnectionString,
            logicalName
         );

         var ids = await _stateTracker.GetSyncedIdsAsync();
         _syncedIds = new ConcurrentDictionary<Guid, byte>(
            ids.Select(id => new KeyValuePair<Guid, byte>(id, 1))
         );
      }

      /// <inheritdoc />
      public async Task SyncAsync(
          IEnumerable<Entity> entities,
          SyncOptions options,
          IProgress<string>? progress = null,
          IProgress<bool>? recordProgress = null,
          CancellationToken ct = default
      )
      {
         var entitiesToSync = entities
             .Where(e => !_syncedIds.ContainsKey(e.Id))
             .ToList();

         if (!entitiesToSync.Any())
            return;

         _recursionTracker.Clear();

         var parallelOptions = new ParallelOptions
         {
            MaxDegreeOfParallelism = options.MaxDegreeOfParallelism,
            CancellationToken = ct
         };

         await Parallel.ForEachAsync(
             entitiesToSync,
             parallelOptions,
             async (entity, token) =>
             {
                try
                {
                   var (success, failureMessage) = await SyncRecordAsync(
                          entity,
                          options,
                          progress,
                          token
                      );

                   if (!success)
                   {
                      var errorMsg = failureMessage ??
                             "Sync failed during record processing.";

                      var pollyCtx = new Polly.Context();
                      if (progress != null)
                         pollyCtx["progress"] = progress;

                      await _retryPolicy.ExecuteAsync(
                          async (ctx) => await _failureLogger
                           .LogFailureToTargetAsync(
                             entity,
                             errorMsg,
                             token
                           ),
                          pollyCtx
                      );
                   }

                   recordProgress?.Report(success);
                }
                catch (Exception ex)
                {
                   _logger.Error(
                          ex,
                          "Error syncing {Entity}:{Id}",
                          entity.LogicalName,
                          entity.Id
                      );

                   var failureMessage = _errorHandler.FormatFailureMessage(
                          "SyncAsync",
                          ex
                      );

                   var pollyCtx = new Polly.Context();
                   if (progress != null)
                      pollyCtx["progress"] = progress;

                   await _retryPolicy.ExecuteAsync(
                       async (ctx) => await _failureLogger.LogFailureToTargetAsync(
                          entity,
                          failureMessage,
                          token
                       ),
                       pollyCtx
                   );

                   recordProgress?.Report(false);
                }
             }
         );
      }

      /// <inheritdoc />
      public async Task<(bool Success, string? FailureMessage)>
          SyncRecordAsync(
              Entity entity,
              SyncOptions options,
              IProgress<string>? progress = null,
              CancellationToken ct = default
          )
      {
         if (_syncedIds.ContainsKey(entity.Id))
         {
            _logger.Debug(
                "Skipping {Entity}:{Id} - Already synced in current state.",
                entity.LogicalName,
                entity.Id
            );

            return (true, string.Empty);
         }

         var recordKey = $"{entity.LogicalName}:{entity.Id}";
         var depth = _recursionTracker.AddOrUpdate(
             recordKey,
             1,
             (_, v) => v + 1
         );

         if (depth > MaxRecursionDepth)
         {
            _logger.Error(
                "Max recursion depth reached for {Key}. Skipping.",
                recordKey
            );

            _recursionTracker.AddOrUpdate(recordKey, 0, (_, v) => v - 1);

            return (false, "Max recursion depth reached.");
         }

         try
         {
            var metadata = await GetMetadataAsync(entity.LogicalName, ct);
            if (metadata == null)
            {
               _logger.Error(
                   "Metadata could not be retrieved for {Entity}. " +
                   "Skipping record {Id}.",
                   entity.LogicalName,
                   entity.Id
               );

               return (
                   false,
                   "Metadata could not be retrieved for " +
                       entity.LogicalName
               );
            }

            if (metadata.IsIntersect == true)
            {
               var result = await SyncIntersectEntityAsync(
                   entity,
                   options,
                   progress,
                   ct
               );

               return result;
            }

            var prepared = await PrepareEntityForTargetAsync(
                entity,
                metadata,
                options,
                ct
            );

            if (options.PreserveDates)
            {
               try
               {
                  await _setupService.PreserveDatesAsync(
                      _target,
                      entity,
                      ct
                  );
               }
               catch (Exception ex)
               {
                  _logger.Warning(
                      ex,
                      "Date preservation failed for {Entity}:{Id}. " +
                      "Continuing sync.",
                      entity.LogicalName,
                      entity.Id
                  );

                  progress?.Report(
                      "Date preservation failed. Continuing..."
                  );
               }
            }

            var (success, failureMessage) =
                await CreateWithFixStrategyAsync(
                    prepared,
                    options,
                    progress,
                    ct
                );

            if (success)
            {
               _syncedIds.TryAdd(entity.Id, 1);
               _idMappingCache[recordKey] = prepared.Id;
               progress?.Report(
                   $"Synced {entity.LogicalName}:{entity.Id}"
               );

               // Mark as synced in the persistent state
               await _stateTracker.MarkAsSyncedAsync(
                   entity.LogicalName,
                   entity.Id
               );

               if (options.PreserveDates)
               {
                  var pollyCtx = new Polly.Context();
                  if (progress != null)
                     pollyCtx["progress"] = progress;

                  await _retryPolicy.ExecuteAsync(
                      async (ctx) => await _setupService.DeleteSourceDateAsync(
                          _target,
                          entity.LogicalName,
                          prepared.Id,
                          ct
                      ),
                      pollyCtx
                  );
               }

               return (true, string.Empty);
            }

            var errorMsg = failureMessage ??
                           $"Failed to sync {entity.LogicalName}:" +
                           $"{entity.Id}.";

            return (false, errorMsg);
         }
         catch (Exception ex)
         {
            _logger.Error(ex, "Failed to sync {Key}", recordKey);
            progress?.Report(
                $"FAILED {entity.LogicalName}:{entity.Id} - " +
                $"{ex.Message}"
            );

            return (false, _errorHandler.FormatFailureMessage(
                "SyncRecordAsync",
                ex
            ));
         }
         finally
         {
            _recursionTracker.AddOrUpdate(recordKey, 0, (_, v) => v - 1);
         }
      }

      private AssociateRequest? CreateAssociateRequest(Entity entity)
      {
         var references = entity.Attributes
             .Values.OfType<EntityReference>().ToList();

         if (references.Count < 2)
         {
            _logger.Warning(
                "Intersect entity {Key} does not have " +
                "two EntityReferences.",
                entity.LogicalName
            );

            return null;
         }

         var request = new AssociateRequest
         {
            Target = references[0],
            Relationship = new Relationship(entity.LogicalName),
            RelatedEntities = new EntityReferenceCollection
                {
                    references[1]
                }
         };

         return request;
      }

      private async Task<(bool success, string failureMessage)>
          HandleSyncExceptionWithRetryAsync(
              Exception ex,
              Entity entity,
              SyncOptions options,
              IProgress<string>? progress,
              CancellationToken ct,
              bool treatAlreadyExistsAsSuccess = false
          )
      {
         if (treatAlreadyExistsAsSuccess &&
             ex.Message.Contains("already exists"))
            return (true, string.Empty);

         var (success, failureMessage) =
             await _errorHandler.HandleSyncExceptionAsync(
                 ex,
                 entity,
                 options,
                 progress,
                 ct,
                 updateFunc: _target.UpdateAsync,
                 statusTransitionFunc: async (
                     entityToTransition,
                     syncOptions,
                     progressReporter,
                     token
                 ) =>
                     await _statusTransitionHandler
                         .HandleStatusTransitionAsync(
                             entityToTransition,
                             syncOptions,
                             progressReporter,
                             token
                         ),
                 resolveMissingDependencyFunc: async (
                     exception,
                     entityWithDependency,
                     syncOptions,
                     progressReporter,
                     token
                 ) =>
                     await _dependencyResolver
                         .ResolveMissingDependencyAsync(
                             exception,
                             entityWithDependency,
                             syncOptions,
                             progressReporter,
                             token,
                             syncRecordFunc: async (
                                 recordToSync,
                                 recordOptions,
                                 recordProgress,
                                 recordToken
                             ) =>
                                 await SyncRecordAsync(
                                     recordToSync,
                                     recordOptions,
                                     recordProgress,
                                     recordToken
                                 ),
                             retryEntityFunc: async (
                                 entityToRetry,
                                 retryOptions,
                                 retryProgress,
                                 retryToken
                             ) =>
                                 await RetryEntityAsync(
                                     entityToRetry,
                                     retryOptions,
                                     retryProgress,
                                     retryToken
                                 ),
                             findExistingFunc: async (
                                 entityToFind,
                                 findToken
                             ) =>
                                 await FindExistingOnTargetAsync(
                                     entityToFind,
                                     findToken
                                 ),
                             idMappingCache: _idMappingCache,
                             triedDependencies: _triedDependencies
                         ),
                 resolveSqlDependencyFunc: async (
                     errorMessage,
                     entityWithSqlDependency,
                     syncOptions,
                     progressReporter,
                     token
                 ) =>
                     await _dependencyResolver
                         .ResolveSqlDependencyAsync(
                             errorMessage,
                             entityWithSqlDependency,
                             syncOptions,
                             progressReporter,
                             token,
                             syncRecordFunc: async (
                                 recordToSync,
                                 recordOptions,
                                 recordProgress,
                                 recordToken
                             ) =>
                                 await SyncRecordAsync(
                                     recordToSync,
                                     recordOptions,
                                     recordProgress,
                                     recordToken
                                 ),
                             retryEntityFunc: async (
                                 entityToRetry,
                                 retryOptions,
                                 retryProgress,
                                 retryToken
                             ) =>
                                 await RetryEntityAsync(
                                     entityToRetry,
                                     retryOptions,
                                     retryProgress,
                                     retryToken
                                 ),
                             findExistingFunc: async (
                                 entityToFind,
                                 findToken
                             ) =>
                                 await FindExistingOnTargetAsync(
                                     entityToFind,
                                     findToken
                                 ),
                             idMappingCache: _idMappingCache
                         ),
                 stripAttributeFunc: StripAttributeAndRetryAsync,
                 findExistingFunc: async (
                     entityToFind,
                     findToken
                 ) =>
                     await FindExistingOnTargetAsync(
                         entityToFind,
                         findToken
                     )
             );

         return success
             ? (true, string.Empty)
             : (false, failureMessage ?? "Unknown error");
      }

      private async Task<(bool success, string failureMessage)>
          SyncIntersectEntityAsync(
              Entity entity,
              SyncOptions options,
              IProgress<string>? progress,
              CancellationToken ct
          )
      {
         try
         {
            var request = CreateAssociateRequest(entity);
            if (request == null)
               return (false, "Invalid N:N relationship record.");

            var context = new Context();
            if (progress != null)
               context["progress"] = progress;

            await _retryPolicy.ExecuteAsync(
                async (ctx) => await _target.ExecuteAsync(request, ct),
                context
            );

            _logger.Information(
                "Associated N:N relationship {Key}",
                entity.LogicalName
            );

            return (true, string.Empty);
         }
         catch (Exception ex)
         {
            return await HandleSyncExceptionWithRetryAsync(
                ex,
                entity,
                options,
                progress,
                ct,
                treatAlreadyExistsAsSuccess: true
            );
         }
      }

      private async Task<(bool success, string failureMessage)>
          CreateWithFixStrategyAsync(
              Entity entity,
              SyncOptions options,
              IProgress<string>? progress,
              CancellationToken ct
          )
      {
         try
         {
            var context = new Context();
            if (progress != null)
               context["progress"] = progress;

            await _retryPolicy.ExecuteAsync(
                async (ctx) => await _target.CreateAsync(entity, ct),
                context
            );

            _logger.Information(
                "Created {Key}:{Id}",
                entity.LogicalName,
                entity.Id
            );

            return (true, string.Empty);
         }
         catch (Exception ex)
         {
            return await HandleSyncExceptionWithRetryAsync(
                ex,
                entity,
                options,
                progress,
                ct
            );
         }
      }
   }
}
