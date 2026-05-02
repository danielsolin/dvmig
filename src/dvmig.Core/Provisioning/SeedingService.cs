using Bogus;
using dvmig.Core.Interfaces;
using dvmig.Core.Shared;
using Microsoft.Xrm.Sdk;
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

      /// <inheritdoc />
      public async Task CleanTestDataAsync(
         IDataverseProvider provider,
         List<string>? entities = null,
         CancellationToken ct = default
      )
      {
         var targetEntities = entities ?? 
            SystemConstants.SyncSettings.RecommendedEntities.ToList();

         foreach (var logicalName in targetEntities)
         {
            _logger.Information($"Cleaning {logicalName}...");

            var query = new QueryExpression(logicalName)
            {
               ColumnSet = new ColumnSet(false)
            };

            var results = await provider.RetrieveMultipleAsync(query, ct);

            foreach (var entity in results.Entities)
            {
               await provider.DeleteAsync(logicalName, entity.Id, ct);
            }
         }

         _logger.Information("Cleanup complete.");
      }
   }
}
