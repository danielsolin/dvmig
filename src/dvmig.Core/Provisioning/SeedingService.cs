using Bogus;
using dvmig.Core.Interfaces;
using Microsoft.Xrm.Sdk;
using static dvmig.Core.Shared.SystemConstants;

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
         _logger.Information(
            $"Seeding {recordCount} Accounts with related data..."
         );

         var faker = new Faker();
         var retryPolicy = _retryService.CreateRetryPolicy();

         var activityTypes = new[]
         {
            DataverseEntities.Task,
            DataverseEntities.PhoneCall,
            DataverseEntities.Email
         };

         for (int i = 0; i < recordCount; i++)
         {
            // 1. Create Account
            var account = new Entity(DataverseEntities.Account);
            account[DataverseAttributes.Name] = 
               faker.Company.CompanyName();
            account[DataverseAttributes.Telephone1] = 
               faker.Phone.PhoneNumber();

            var accountId = await retryPolicy.ExecuteAsync(
               async () => await provider.CreateAsync(account, ct)
            );

            // 2. Create 2-7 Contacts per Account
            var contactsInAccount = new List<Guid>();
            int contactCount = faker.Random.Int(2, 7);

            for (int j = 0; j < contactCount; j++)
            {
               var contact = new Entity(
                  DataverseEntities.Contact
               );

               contact[DataverseAttributes.FirstName] = 
                  faker.Name.FirstName();
               contact[DataverseAttributes.LastName] = 
                  faker.Name.LastName();
               contact[DataverseAttributes.EmailAddress1] = 
                  faker.Internet.Email();

               contact[DataverseAttributes.ParentCustomerId] =
                  new EntityReference(
                     DataverseEntities.Account,
                     accountId
                  );

               var contactId = await retryPolicy.ExecuteAsync(
                  async () => await provider.CreateAsync(contact, ct)
               );

               contactsInAccount.Add(contactId);
            }

            // Set Primary Contact on Account
            var primaryContactId = faker.PickRandom(contactsInAccount);
            var accountUpdate = new Entity(
               DataverseEntities.Account, 
               accountId
            );

            accountUpdate[DataverseAttributes.PrimaryContactId] =
               new EntityReference(
                  DataverseEntities.Contact, 
                  primaryContactId
               );

            await retryPolicy.ExecuteAsync(
               async () => await provider.UpdateAsync(accountUpdate, ct)
            );

            // 3. Create 5-12 Activities per Account
            int activityCount = faker.Random.Int(5, 12);

            for (int k = 0; k < activityCount; k++)
            {
               var logicalName = faker.PickRandom(activityTypes);
               var activity = new Entity(logicalName);

               activity[DataverseAttributes.Subject] = 
                  faker.Lorem.Sentence(5);
               activity[DataverseAttributes.Description] = 
                  faker.Lorem.Paragraph();
               activity[DataverseAttributes.ScheduledEnd] = 
                  faker.Date.Future();

               // Randomly relate to Account or a random Contact 
               // within that Account
               var regardingAttr = 
                  DataverseAttributes.RegardingObjectId;

               if (faker.Random.Bool())
               {
                  activity[regardingAttr] = new EntityReference(
                     DataverseEntities.Account, 
                     accountId
                  );
               }
               else
               {
                  activity[regardingAttr] = new EntityReference(
                     DataverseEntities.Contact, 
                     faker.PickRandom(contactsInAccount)
                  );
               }

               await retryPolicy.ExecuteAsync(
                  async () => await provider.CreateAsync(activity, ct)
               );
            }

            _logger.Information(
               $"Account {i + 1}/{recordCount} seeded with " +
               $"{contactCount} contacts and {activityCount} activities."
            );
         }

         _logger.Information("Seeding complete.");
      }
   }
}
