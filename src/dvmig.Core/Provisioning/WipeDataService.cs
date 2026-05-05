using dvmig.Core.Interfaces;
using dvmig.Core.Shared;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace dvmig.Core.Provisioning
{
   /// <summary>
   /// Implementation of <see cref="IWipeDataService"/> that handles 
   /// efficient, parallel data removal from Dataverse environments.
   /// </summary>
   public class WipeDataService : IWipeDataService
   {
      private readonly ILogger _logger;
      private readonly IRetryService _retryService;

      /// <summary>
      /// Initializes a new instance of the
      /// <see cref="WipeDataService"/> class.
      /// </summary>
      /// <param name="logger">The logger instance.</param>
      /// <param name="retryService">The retry service.</param>
      public WipeDataService(ILogger logger, IRetryService retryService)
      {
         _logger = logger;
         _retryService = retryService;
      }

      /// <inheritdoc />
      public async Task WipeEntitiesAsync(
         IDataverseProvider provider,
         List<string>? entities = null,
         IProgress<long>? progress = null,
         CancellationToken ct = default
      )
      {
         var targetEntities = entities ??
            SystemConstants.SyncSettings.RecommendedEntities.ToList();

         // Reverse to handle potential simple dependencies 
         // (e.g., delete contacts before accounts if needed)
         targetEntities.Reverse();

         long totalDeleted = 0;
         long initialTotal = 0;

         foreach (var entity in targetEntities)
            initialTotal += await provider.GetRecordCountAsync(entity, ct);

         progress?.Report(initialTotal);

         foreach (var logicalName in targetEntities)
         {
            totalDeleted += await WipeEntityRecordsAsync(
               provider,
               logicalName,
               initialTotal,
               totalDeleted,
               progress,
               ct
            );
         }

         _logger.Information("Cleanup complete.");
      }

      private async Task<long> WipeEntityRecordsAsync(
         IDataverseProvider provider,
         string logicalName,
         long initialTotal,
         long alreadyDeletedTotal,
         IProgress<long>? progress,
         CancellationToken ct
      )
      {
         _logger.Information($"Cleaning {logicalName}...");

         var lockObject = new object();
         var deletedForThisEntity = 0L;

         while (true)
         {
            var query = new QueryExpression(logicalName)
            {
               ColumnSet = new ColumnSet(false),
               TopCount = 1000
            };

            var results = await provider.RetrieveMultipleAsync(query, ct);

            if (results.Entities.Count == 0)
               break;

            var chunks = results.Entities
               .Select((e, i) => new { Entity = e, Index = i })
               .GroupBy(x => x.Index / 100)
               .Select(g => g.Select(x => x.Entity).ToList())
               .ToList();

            var parallelOptions = new ParallelOptions
            {
               MaxDegreeOfParallelism = 10,
               CancellationToken = ct
            };

            await Parallel.ForEachAsync(
               chunks,
               parallelOptions,
               async (chunk, token) =>
               {
                  await DeleteRecordBatchAsync(provider, chunk, token);

                  lock (lockObject)
                  {
                     deletedForThisEntity += chunk.Count;

                     progress?.Report(
                        Math.Max(
                           0,
                           (
                              initialTotal -
                              (alreadyDeletedTotal + deletedForThisEntity)
                           )
                        )
                     );
                  }
               }
            );
         }

         return deletedForThisEntity;
      }

      private async Task DeleteRecordBatchAsync(
         IDataverseProvider provider,
         List<Entity> chunk,
         CancellationToken ct
      )
      {
         var multipleRequest = new ExecuteMultipleRequest
         {
            Settings = new ExecuteMultipleSettings
            {
               ContinueOnError = true,
               ReturnResponses = false
            },
            Requests = new OrganizationRequestCollection()
         };

         foreach (var entity in chunk)
            multipleRequest.Requests.Add(new DeleteRequest
            {
               Target = entity.ToEntityReference()
            });

         var retryPolicy = _retryService.CreateRetryPolicy();

         await retryPolicy.ExecuteAsync(
            async () => await provider.ExecuteAsync(
               multipleRequest,
               ct
            )
         );
      }
   }
}
