using Bogus;
using dvmig.Core.Interfaces;
using dvmig.Core.Logging;
using dvmig.Core.Shared;
using Microsoft.Xrm.Sdk;
using Serilog;

namespace dvmig.Core.Provisioning
{
   /// <summary>
   /// Implementation of the test data seeder using the Bogus library for 
   /// realistic data generation.
   /// </summary>
   public class TestDataSeeder : ITestDataSeeder
   {
      private readonly ILogger _logger;
      private readonly IRetryStrategy _retryStrategy;
      private readonly Polly.Retry.AsyncRetryPolicy _retryPolicy;

      /// <summary>
      /// Initializes a new instance of the 
      /// <see cref="TestDataSeeder"/> class.
      /// </summary>
      /// <param name="logger">The logger instance.</param>
      /// <param name="retryStrategy">The retry strategy.</param>
      public TestDataSeeder(ILogger logger, IRetryStrategy retryStrategy)
      {
         _logger = logger;
         _retryStrategy = retryStrategy;
         _retryPolicy = _retryStrategy.CreateRetryPolicy();
      }

      /// <inheritdoc />
      public async Task SeedTestDataAsync(
          IDataverseProvider provider,
          int count,
          IProgress<string>? progress = null,
          CancellationToken ct = default
      )
      {
         _logger.Information(
             "Starting interconnected test data seeding: {Count} accounts",
             count
         );
         progress?.Report(
             $"Generating {count} accounts with related contacts..."
         );

         var accountFaker = new Faker<Entity>()
             .CustomInstantiator(f => new Entity(
                SystemConstants.DataverseEntities.Account
             ))
             .FinishWith((f, e) =>
             {
                e[SystemConstants.DataverseAttributes.Name] =
                   f.Company.CompanyName();
                e[SystemConstants.DataverseAttributes.Telephone1] =
                   f.Phone.PhoneNumber();
                e[SystemConstants.DataverseAttributes.Address1Line1] =
                   f.Address.StreetAddress();
                e[SystemConstants.DataverseAttributes.Address1City] =
                   f.Address.City();
                e[SystemConstants.DataverseAttributes.Address1PostalCode] =
                   f.Address.ZipCode();
                e[SystemConstants.DataverseAttributes.WebsiteUrl] =
                   f.Internet.Url();
             });

         var contactFaker = new Faker<Entity>()
             .CustomInstantiator(f => new Entity(
                SystemConstants.DataverseEntities.Contact
             ))
             .FinishWith((f, e) =>
             {
                e[SystemConstants.DataverseAttributes.FirstName] =
                   f.Name.FirstName();
                e[SystemConstants.DataverseAttributes.LastName] =
                   f.Name.LastName();
                e[SystemConstants.DataverseAttributes.EmailAddress1] =
                   f.Internet.Email();
                e[SystemConstants.DataverseAttributes.Telephone1] =
                   f.Phone.PhoneNumber();
                e[SystemConstants.DataverseAttributes.JobTitle] =
                   f.Name.JobTitle();
             });

         var taskFaker = new Faker<Entity>()
             .CustomInstantiator(f => new Entity(
                SystemConstants.DataverseEntities.Task
             ))
             .FinishWith((f, e) =>
             {
                e[SystemConstants.DataverseAttributes.Subject] =
                   f.Lorem.Sentence();
                e[SystemConstants.DataverseAttributes.Description] =
                   f.Lorem.Paragraph();
                e[SystemConstants.DataverseAttributes.ScheduledEnd] =
                   f.Date.Soon();
             });

         var phoneCallFaker = new Faker<Entity>()
             .CustomInstantiator(f => new Entity(
                SystemConstants.DataverseEntities.PhoneCall
             ))
             .FinishWith((f, e) =>
             {
                e[SystemConstants.DataverseAttributes.Subject] =
                   $"Follow up: {f.Company.CatchPhrase()}";
                e[SystemConstants.DataverseAttributes.Description] =
                   f.Lorem.Sentences(2);
                e[SystemConstants.DataverseAttributes.PhoneNumber] =
                   f.Phone.PhoneNumber();
             });

         var emailFaker = new Faker<Entity>()
             .CustomInstantiator(f => new Entity(
                SystemConstants.DataverseEntities.Email
             ))
             .FinishWith((f, e) =>
             {
                e[SystemConstants.DataverseAttributes.Subject] =
                   f.Commerce.ProductName();
                e[SystemConstants.DataverseAttributes.Description] =
                       string.Join("\n", f.Lorem.Paragraphs(3));
             });

         var random = new Random();
         int totalContactsCreated = 0;
         int totalActivitiesCreated = 0;

         for (int i = 0; i < count; i++)
         {
            ct.ThrowIfCancellationRequested();

            // 1. Create Account
            var account = accountFaker.Generate();
            var accountId = await provider.CreateAsync(account, ct);
            account.Id = accountId;
            var accountRef = new EntityReference(
               SystemConstants.DataverseEntities.Account,
               accountId
            );

            // 2. Create related Contacts
            int contactCount = random.Next(1, 11);
            var relatedContacts = contactFaker.Generate(contactCount);
            var createdContactIds = new List<Guid>();

            foreach (var contact in relatedContacts)
            {
               contact[SystemConstants.DataverseAttributes.ParentCustomerId] =
                  accountRef;
               var contactId = await provider.CreateAsync(contact, ct);
               createdContactIds.Add(contactId);
               totalContactsCreated++;
            }

            // 3. Set Primary Contact
            var primaryContactId = createdContactIds[
                random.Next(createdContactIds.Count)
            ];
            var accountUpdate = new Entity(
               SystemConstants.DataverseEntities.Account,
               accountId
            );
            accountUpdate[SystemConstants.DataverseAttributes.PrimaryContactId] =
               new EntityReference(
                  SystemConstants.DataverseEntities.Contact,
                  primaryContactId
               );

            await provider.UpdateAsync(accountUpdate, ct);

            // 4. Create Activities regarding the Account

            // Tasks
            int taskCount = random.Next(1, 6);
            foreach (var task in taskFaker.Generate(taskCount))
            {
               task[SystemConstants.DataverseAttributes.RegardingObjectId] =
                  accountRef;
               await provider.CreateAsync(task, ct);
               totalActivitiesCreated++;
            }

            // Phone Calls
            int phoneCount = random.Next(1, 4);
            foreach (var phone in phoneCallFaker.Generate(phoneCount))
            {
               phone[SystemConstants.DataverseAttributes.RegardingObjectId] =
                  accountRef;

               // To: Random related contact
               var toRef = new EntityReference(
                   SystemConstants.DataverseEntities.Contact,
                   createdContactIds[random.Next(createdContactIds.Count)]
               );
               phone[SystemConstants.DataverseAttributes.To] =
                  CreatePartyList(toRef);

               await provider.CreateAsync(phone, ct);
               totalActivitiesCreated++;
            }

            // Emails
            int emailCount = random.Next(1, 3);
            foreach (var email in emailFaker.Generate(emailCount))
            {
               email[SystemConstants.DataverseAttributes.RegardingObjectId] =
                  accountRef;

               // To: Primary contact
               var toRef = new EntityReference(
                   SystemConstants.DataverseEntities.Contact,
                   primaryContactId
               );
               email[SystemConstants.DataverseAttributes.To] =
                  CreatePartyList(toRef);

               await provider.CreateAsync(email, ct);
               totalActivitiesCreated++;
            }

            if ((i + 1) % 5 == 0 || i + 1 == count)
            {
               var msg = $"Processed {i + 1}/{count} accounts. " +
                   $"Contacts: {totalContactsCreated}, " +
                   $"Activities: {totalActivitiesCreated}";

               _logger.Information(progress, msg);
            }
         }

         _logger.Information(
             "Seeding completed. Accounts: {AccountCount}, " +
             "Contacts: {ContactCount}, Activities: {ActivityCount}",
             count,
             totalContactsCreated,
             totalActivitiesCreated
         );
         progress?.Report(
             $"Seeding completed. Created {count} accounts, " +
             $"{totalContactsCreated} contacts, and " +
             $"{totalActivitiesCreated} activities."
         );
      }

      private EntityCollection CreatePartyList(EntityReference reference)
      {
         var party = new Entity(SystemConstants.DataverseEntities.ActivityParty);
         party[SystemConstants.DataverseAttributes.PartyId] = reference;

         return new EntityCollection(new List<Entity> { party });
      }

      /// <inheritdoc />
      public async Task CleanTestDataAsync(
          IDataverseProvider provider,
          List<string>? entitiesToWipe = null,
          IProgress<string>? progress = null,
          CancellationToken ct = default
      )
      {
         _logger.Warning(
             "Starting data cleanup..."
         );
         progress?.Report("Starting data cleanup...");

         var entitiesToDelete = entitiesToWipe ?? new List<string>
         {
             SystemConstants.DataverseEntities.Email,
             SystemConstants.DataverseEntities.PhoneCall,
             SystemConstants.DataverseEntities.Task,
             SystemConstants.DataverseEntities.Contact,
             SystemConstants.DataverseEntities.Account
         };

         // If using the default list, we rely on the pre-defined order 
         // (activities first, then contacts, then accounts) to avoid 
         // dependency errors. If a custom list is provided, we process 
         // them in the order given.
         foreach (var entity in entitiesToDelete)
         {
            await DeleteAllOfEntityAsync(provider, entity, progress, ct);
         }

         _logger.Information(progress, "Cleanup completed successfully.");
      }

      private async Task DeleteAllOfEntityAsync(
          IDataverseProvider provider,
          string logicalName,
          IProgress<string>? progress,
          CancellationToken ct
      )
      {
         progress?.Report($"Fetching {logicalName} records for deletion...");

         var query = new Microsoft.Xrm.Sdk.Query.QueryExpression(logicalName)
         {
            ColumnSet = new Microsoft.Xrm.Sdk.Query.ColumnSet(false),
            PageInfo = new Microsoft.Xrm.Sdk.Query.PagingInfo
            {
               Count = dvmig.Core.Shared.SystemConstants.AppConstants
                     .DefaultDeletionBatchSize,
               PageNumber = 1
            }
         };

         int totalDeleted = 0;
         while (true)
         {
            var results = await provider.RetrieveMultipleAsync(query, ct);
            if (results.Entities.Count == 0)
               break;

            progress?.Report(
                $"Deleting {results.Entities.Count} {logicalName} " +
                $"records (Page {query.PageInfo.PageNumber})..."
            );

            var parallelOptions = new ParallelOptions
            {
               MaxDegreeOfParallelism = 10,
               CancellationToken = ct
            };

            var context = new Polly.Context();
            if (progress != null)
               context["progress"] = progress;

            await Parallel.ForEachAsync(
                results.Entities,
                parallelOptions,
                async (entity, token) =>
                {
                   await _retryPolicy.ExecuteAsync(
                           async (ctx) =>
                               await provider.DeleteAsync(
                                   logicalName,
                                   entity.Id,
                                   token
                               ),
                           context
                       );

                   Interlocked.Increment(ref totalDeleted);
                }
            );

            progress?.Report(
                $"Deletion progress ({logicalName}): " +
                $"{totalDeleted} total deleted."
            );

            if (!results.MoreRecords)
               break;

            query.PageInfo.PageNumber++;
            query.PageInfo.PagingCookie = results.PagingCookie;
         }

         if (totalDeleted == 0)
            progress?.Report($"No {logicalName} records found.");

         _logger.Information(
             "Deleted {Count} records of type {Entity}",
             totalDeleted,
             logicalName
         );
      }
   }
}
