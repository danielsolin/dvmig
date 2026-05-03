using Bogus;
using dvmig.Core.Interfaces;
using dvmig.Core.Shared;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace dvmig.Core.Provisioning
{
   /// <summary>
   /// Implementation of <see cref="ISeedingService"/> that seeds and cleans 
   /// test data in Dataverse.
   /// </summary>
   public class SeedingService : ISeedingService
   {
      private readonly ILogger _logger;
      private readonly IRetryService _retryService;

      /// <summary>
      /// Initializes a new instance of the <see cref="SeedingService"/> class.
      /// </summary>
      /// <param name="logger">The logger instance.</param>
      /// <param name="retryService">The retry service.</param>
      public SeedingService(ILogger logger, IRetryService retryService)
      {
         _logger = logger;
         _retryService = retryService;
      }

      /// <inheritdoc />
      public async Task SeedSampleDataAsync(
         IDataverseProvider provider,
         int recordCount = 50,
         CancellationToken ct = default
      )
      {
         _logger.Information($"Seeding {recordCount} records per entity...");

         var faker = new Faker();
         var retryPolicy = _retryService.CreateRetryPolicy();

         var entities = new[] { "account", "contact" };

         foreach (var logicalName in entities)
         {
            for (int i = 0; i < recordCount; i++)
            {
               var entity = new Entity(logicalName);
               if (logicalName == "account")
               {
                  entity["name"] = faker.Company.CompanyName();
                  entity["telephone1"] = faker.Phone.PhoneNumber();
               }
               else
               {
                  entity["firstname"] = faker.Name.FirstName();
                  entity["lastname"] = faker.Name.LastName();
                  entity["emailaddress1"] = faker.Internet.Email();
               }

               await retryPolicy.ExecuteAsync(
                  async () => await provider.CreateAsync(entity, ct)
               );
            }
         }

         _logger.Information("Seeding complete.");
      }

      //DMSFIX: Should the cleanup logic be here in the seeding class?
      /// <inheritdoc />
      public async Task CleanTestDataAsync(
         IDataverseProvider provider,
         List<string>? entities = null,
         IProgress<long>? progress = null,
         CancellationToken ct = default
      )
      {
         var targetEntities = entities ??
            SystemConstants.SyncSettings.RecommendedEntities.ToList();

         targetEntities.Reverse();

         long totalDeleted = 0;
         long initialTotal = 0;
         var lockObject = new object();

         foreach (var entity in targetEntities)
            initialTotal += await provider.GetRecordCountAsync(entity, ct);

         progress?.Report(initialTotal);

         foreach (var logicalName in targetEntities)
         {
            _logger.Information($"Cleaning {logicalName}...");

            while (true)
            {
               var query = new QueryExpression(logicalName)
               {
                  ColumnSet = new ColumnSet(false),
                  TopCount = 1000 // Fetch 1000 at a time to process in 10 parallel batches of 100
               };

               var results = await provider.RetrieveMultipleAsync(query, ct);
               if (results.Entities.Count == 0)
                  break;

               // Split the 1000 records into chunks of 100 for parallel batching
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

               var retryPolicy = _retryService.CreateRetryPolicy();

               await Parallel.ForEachAsync(
                  chunks,
                  parallelOptions,
                  async (chunk, token) =>
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

                     await retryPolicy.ExecuteAsync(
                        async () => await provider.ExecuteAsync(
                           multipleRequest, 
                           token
                        )
                     );

                     lock (lockObject)
                     {
                        totalDeleted += chunk.Count;
                        progress?.Report(
                           Math.Max(0, initialTotal - totalDeleted)
                        );
                     }
                  }
               );
            }
         }

         _logger.Information("Cleanup complete.");
      }
   }
}
