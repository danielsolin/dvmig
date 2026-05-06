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
      private readonly IUserService _userService;

      /// <summary>
      /// Initializes a new instance of the <see cref="SeedingService"/> class.
      /// </summary>
      /// <param name="logger">The logger instance.</param>
      /// <param name="userService">The user service instance.</param>
      public SeedingService(ILogger logger, IUserService userService)
      {
         _logger = logger;
         _userService = userService;
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
         var availableUserIds = await _userService.GetRealActiveUsersAsync(
            provider,
            ct
         );

         var activityTypes = DataverseEntities.ToList()
            .Where(e => e.IsActivityEntity)
            .Select(e => e.Name)
            .ToArray();

         for (int i = 0; i < recordCount; i++)
         {
            // 1. Create Account
            var account = new Entity(DataverseEntities.Account.Name);
            account[DataverseAttributes.Name] =
               faker.Company.CompanyName();
            account[DataverseAttributes.Telephone1] =
               faker.Phone.PhoneNumber();

            // Randomize audit fields
            var createdDate = faker.Date.Past(2);
            var modifiedDate = faker.Date.Between(
               createdDate,
               DateTime.UtcNow
            );

            account[DataverseAttributes.OverriddenCreatedOn] = createdDate;
            account[DataverseAttributes.ModifiedOn] = modifiedDate;

            var accountCallerId = availableUserIds.Count > 0
               ? faker.PickRandom(availableUserIds)
               : (Guid?)null;

            if (accountCallerId.HasValue)
            {
               account[DataverseAttributes.CreatedBy] = new EntityReference(
                  DataverseEntities.SystemUser.Name,
                  accountCallerId.Value
               );
            }

            var accountId = await provider.CreateAsync(
               account,
               ct,
               accountCallerId
            );

            // 2. Create 2-7 Contacts per Account
            var contactsInAccount = new List<Guid>();
            int contactCount = faker.Random.Int(2, 7);

            for (int j = 0; j < contactCount; j++)
            {
               var contact = new Entity(
                  DataverseEntities.Contact.Name
               );

               contact[DataverseAttributes.FirstName] =
                  faker.Name.FirstName();
               contact[DataverseAttributes.LastName] =
                  faker.Name.LastName();
               contact[DataverseAttributes.EmailAddress1] =
                  faker.Internet.Email();

               contact[DataverseAttributes.ParentCustomerId] =
                  new EntityReference(
                     DataverseEntities.Account.Name,
                     accountId
                  );

               // Randomize audit fields
               var contactCreatedDate = faker.Date.Between(
                  createdDate,
                  DateTime.UtcNow
               );

               var contactModifiedDate = faker.Date.Between(
                  contactCreatedDate,
                  DateTime.UtcNow
               );

               contact[DataverseAttributes.OverriddenCreatedOn] =
                  contactCreatedDate;

               contact[DataverseAttributes.ModifiedOn] = contactModifiedDate;

               var contactCallerId = availableUserIds.Count > 0
                  ? faker.PickRandom(availableUserIds)
                  : (Guid?)null;

               if (contactCallerId.HasValue)
               {
                  contact[DataverseAttributes.CreatedBy] = new EntityReference(
                     DataverseEntities.SystemUser.Name,
                     contactCallerId.Value
                  );
               }

               var contactId = await provider.CreateAsync(
                  contact,
                  ct,
                  contactCallerId
               );

               contactsInAccount.Add(contactId);
            }

            // Set Primary Contact on Account
            var primaryContactId = faker.PickRandom(contactsInAccount);
            var accountUpdate = new Entity(
               DataverseEntities.Account.Name,
               accountId
            );

            accountUpdate[DataverseAttributes.PrimaryContactId] =
               new EntityReference(
                  DataverseEntities.Contact.Name,
                  primaryContactId
               );

            // Update modified date on account update
            accountUpdate[DataverseAttributes.ModifiedOn] = DateTime.UtcNow;

            await provider.UpdateAsync(
               accountUpdate,
               ct,
               accountCallerId
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

               // Randomize audit fields
               var activityCreatedDate = faker.Date.Between(
                  createdDate,
                  DateTime.UtcNow
               );

               var activityModifiedDate = faker.Date.Between(
                  activityCreatedDate,
                  DateTime.UtcNow
               );

               activity[DataverseAttributes.OverriddenCreatedOn] =
                  activityCreatedDate;

               activity[DataverseAttributes.ModifiedOn] = activityModifiedDate;

               // Randomly relate to Account or a random Contact 
               // within that Account
               var regardingAttr =
                  DataverseAttributes.RegardingObjectId;

               if (faker.Random.Bool())
               {
                  activity[regardingAttr] = new EntityReference(
                     DataverseEntities.Account.Name,
                     accountId
                  );
               }
               else
               {
                  activity[regardingAttr] = new EntityReference(
                     DataverseEntities.Contact.Name,
                     faker.PickRandom(contactsInAccount)
                  );
               }

               var activityCallerId = availableUserIds.Count > 0
                  ? faker.PickRandom(availableUserIds)
                  : (Guid?)null;

               if (activityCallerId.HasValue)
               {
                  activity[DataverseAttributes.CreatedBy] =
                     new EntityReference(
                        DataverseEntities.SystemUser.Name,
                        activityCallerId.Value
                     );
               }

               await provider.CreateAsync(
                  activity,
                  ct,
                  activityCallerId
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
