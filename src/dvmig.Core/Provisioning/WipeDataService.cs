using dvmig.Core.Interfaces;
using dvmig.Core.Providers;
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

      /// <summary>
      /// Initializes a new instance of the
      /// <see cref="WipeDataService"/> class.
      /// </summary>
      /// <param name="logger">The logger instance.</param>
      public WipeDataService(ILogger logger)
      {
         _logger = logger;
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
         var consecutiveFailures = 0;
         const int MaxConsecutiveFailures = 3;

         while (consecutiveFailures < MaxConsecutiveFailures)
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

            var deletedInThisIteration = 0L;

#if NET48
            // Task-based alternative for .NET Framework 4.8
            using(var semaphore = new SemaphoreSlim(parallelOptions.MaxDegreeOfParallelism))
            {
               var tasks = chunks.Select(async chunk =>
               {
                  await semaphore.WaitAsync(ct);
                  try
                  {
                     var successfulCount = await DeleteRecordBatchWithCountAsync(
                        provider,
                        chunk,
                        ct
                     );

                     lock(lockObject)
                     {
                        deletedForThisEntity += successfulCount;
                        deletedInThisIteration += successfulCount;

                        progress?.Report(
                           Math.Max(
                              0,
                              initialTotal -
                              (alreadyDeletedTotal + deletedForThisEntity)
                           )
                        );
                     }
                  }
                  finally
                  {
                     semaphore.Release();
                  }
               });

               await Task.WhenAll(tasks);
            }
#else
            await Parallel.ForEachAsync(
               chunks,
               parallelOptions,
               async (chunk, token) =>
               {
                  var successfulCount = await DeleteRecordBatchWithCountAsync(
                     provider,
                     chunk,
                     token
                  );

                  lock (lockObject)
                  {
                     deletedForThisEntity += successfulCount;
                     deletedInThisIteration += successfulCount;

                     progress?.Report(
                        Math.Max(
                           0,
                           initialTotal -
                           (alreadyDeletedTotal + deletedForThisEntity)
                        )
                     );
                  }
               }
            );
#endif

            // If we didn't manage to delete anything in this whole iteration 
            // of 1000 records, something is wrong (likely constraints).
            if (deletedInThisIteration == 0)
            {
               consecutiveFailures++;
               
               if (consecutiveFailures < MaxConsecutiveFailures)
                  _logger.Warning(
                     $"Could not delete any records for {logicalName} " +
                     "in this batch. It might be due to dependencies. " +
                     $"Retry {consecutiveFailures}/{MaxConsecutiveFailures}..."
                  );
            }
            else
               consecutiveFailures = 0; // Reset on success
         }

         if (consecutiveFailures >= MaxConsecutiveFailures)
            _logger.Error(
               $"Skipping remaining records for {logicalName} after " +
               $"{MaxConsecutiveFailures} failed attempts. " +
               "This is usually caused by circular dependencies or " +
               "mandatory relationships."
            );

         return deletedForThisEntity;
      }

      private async Task<long> DeleteRecordBatchWithCountAsync(
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
               ReturnResponses = true // We need responses to count successes
            },
            Requests = new OrganizationRequestCollection()
         };

         foreach (var entity in chunk)
            multipleRequest.Requests.Add(new DeleteRequest
            {
               Target = entity.ToEntityReference()
            });

         var response = (ExecuteMultipleResponse)await provider
            .ExecuteAsync(multipleRequest, ct);

         // Count records that didn't return an error
         return response.Responses.Count(r => r.Fault == null);
      }
   }
}
