using Bogus;
using dvmig.Core.Interfaces;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
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

         // Identify users for impersonation
         var availableUserIds = await GetAvailableUserIdsAsync(provider, ct);
         if (availableUserIds.Any())
         {
            _logger.Information(
               "Found {Count} users for impersonation.",
               availableUserIds.Count
            );
         }

         var userIds = availableUserIds.Values.ToList();
         var originalCallerId = provider.CallerId;

         var activityTypes = new[]
         {
            DataverseEntities.Task,
            DataverseEntities.PhoneCall,
            DataverseEntities.Email
         };

         try
         {
            for (int i = 0; i < recordCount; i++)
            {
               // Randomly impersonate for each account and its children
               if (userIds.Any())
                  provider.CallerId = faker.PickRandom(userIds);

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
         }
         finally
         {
            provider.CallerId = originalCallerId;
         }

         _logger.Information("Seeding complete.");
      }

      private async Task<Dictionary<string, Guid>> GetAvailableUserIdsAsync(
         IDataverseProvider provider,
         CancellationToken ct
      )
      {
         var results = new Dictionary<string, Guid>();

         var query = new QueryExpression(DataverseEntities.SystemUser)
         {
            ColumnSet = new ColumnSet(
               DataverseAttributes.SystemUserId,
               DataverseAttributes.FullName,
               DataverseAttributes.FirstName
            ),
            Criteria = new FilterExpression
            {
               FilterOperator = LogicalOperator.And
            }
         };

         // Filter for enabled users with Read-Write access mode
         query.Criteria.AddCondition(
            DataverseAttributes.IsDisabled,
            ConditionOperator.Equal,
            false
         );

         query.Criteria.AddCondition(
            DataverseAttributes.AccessMode,
            ConditionOperator.Equal,
            0 // Read-Write
         );

         try
         {
            var users = await provider.RetrieveMultipleAsync(query, ct);
            foreach (var user in users.Entities)
            {
               var fullName = user.GetAttributeValue<string>(
                  DataverseAttributes.FullName
               );

               if (!string.IsNullOrEmpty(fullName))
                  results[fullName] = user.Id;
            }
         }
         catch (Exception ex)
         {
            _logger.Warning(ex, "Could not retrieve users for seeding.");
         }

         return results;
      }
   }
}

