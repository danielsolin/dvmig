using Bogus;
using dvmig.Core.Interfaces;
using Microsoft.Xrm.Sdk;
using Serilog;

namespace dvmig.Core.Seeding
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
                .CustomInstantiator(f => new Entity("account"))
                .FinishWith((f, e) =>
                {
                    e["name"] = f.Company.CompanyName();
                    e["telephone1"] = f.Phone.PhoneNumber();
                    e["address1_line1"] = f.Address.StreetAddress();
                    e["address1_city"] = f.Address.City();
                    e["address1_postalcode"] = f.Address.ZipCode();
                    e["websiteurl"] = f.Internet.Url();
                });

            var contactFaker = new Faker<Entity>()
                .CustomInstantiator(f => new Entity("contact"))
                .FinishWith((f, e) =>
                {
                    e["firstname"] = f.Name.FirstName();
                    e["lastname"] = f.Name.LastName();
                    e["emailaddress1"] = f.Internet.Email();
                    e["telephone1"] = f.Phone.PhoneNumber();
                    e["jobtitle"] = f.Name.JobTitle();
                });

            var taskFaker = new Faker<Entity>()
                .CustomInstantiator(f => new Entity("task"))
                .FinishWith((f, e) =>
                {
                    e["subject"] = f.Lorem.Sentence();
                    e["description"] = f.Lorem.Paragraph();
                    e["scheduledend"] = f.Date.Soon();
                });

            var phoneCallFaker = new Faker<Entity>()
                .CustomInstantiator(f => new Entity("phonecall"))
                .FinishWith((f, e) =>
                {
                    e["subject"] = $"Follow up: {f.Company.CatchPhrase()}";
                    e["description"] = f.Lorem.Sentences(2);
                    e["phonenumber"] = f.Phone.PhoneNumber();
                });

            var emailFaker = new Faker<Entity>()
                .CustomInstantiator(f => new Entity("email"))
                .FinishWith((f, e) =>
                {
                    e["subject"] = f.Commerce.ProductName();
                    e["description"] =
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
                var accountRef = new EntityReference("account", accountId);

                // 2. Create related Contacts
                int contactCount = random.Next(1, 11);
                var relatedContacts = contactFaker.Generate(contactCount);
                var createdContactIds = new List<Guid>();

                foreach (var contact in relatedContacts)
                {
                    contact["parentcustomerid"] = accountRef;
                    var contactId = await provider.CreateAsync(contact, ct);
                    createdContactIds.Add(contactId);
                    totalContactsCreated++;
                }

                // 3. Set Primary Contact
                var primaryContactId = createdContactIds[
                    random.Next(createdContactIds.Count)
                ];
                var accountUpdate = new Entity("account", accountId);
                accountUpdate["primarycontactid"] = new EntityReference(
                    "contact",
                    primaryContactId
                );

                await provider.UpdateAsync(accountUpdate, ct);

                // 4. Create Activities regarding the Account

                // Tasks
                int taskCount = random.Next(1, 6);
                foreach (var task in taskFaker.Generate(taskCount))
                {
                    task["regardingobjectid"] = accountRef;
                    await provider.CreateAsync(task, ct);
                    totalActivitiesCreated++;
                }

                // Phone Calls
                int phoneCount = random.Next(1, 4);
                foreach (var phone in phoneCallFaker.Generate(phoneCount))
                {
                    phone["regardingobjectid"] = accountRef;

                    // To: Random related contact
                    var toRef = new EntityReference(
                        "contact",
                        createdContactIds[random.Next(createdContactIds.Count)]
                    );
                    phone["to"] = CreatePartyList(toRef);

                    await provider.CreateAsync(phone, ct);
                    totalActivitiesCreated++;
                }

                // Emails
                int emailCount = random.Next(1, 3);
                foreach (var email in emailFaker.Generate(emailCount))
                {
                    email["regardingobjectid"] = accountRef;

                    // To: Primary contact
                    var toRef = new EntityReference(
                        "contact",
                        primaryContactId
                    );
                    email["to"] = CreatePartyList(toRef);

                    await provider.CreateAsync(email, ct);
                    totalActivitiesCreated++;
                }

                if ((i + 1) % 5 == 0 || i + 1 == count)
                {
                    var msg = $"Processed {i + 1}/{count} accounts. " +
                        $"Contacts: {totalContactsCreated}, " +
                        $"Activities: {totalActivitiesCreated}";

                    _logger.Information(msg);
                    progress?.Report(msg);
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
            var party = new Entity("activityparty");
            party["partyid"] = reference;

            return new EntityCollection(new List<Entity> { party });
        }

        /// <inheritdoc />
        public async Task CleanTestDataAsync(
            IDataverseProvider provider,
            IProgress<string>? progress = null,
            CancellationToken ct = default
        )
        {
            _logger.Warning(
                "Starting test data cleanup (Activities, Contacts, Accounts)..."
            );
            progress?.Report("Cleaning up activities...");

            // Delete in order of dependency
            await DeleteAllOfEntityAsync(provider, "email", progress, ct);
            await DeleteAllOfEntityAsync(provider, "phonecall", progress, ct);
            await DeleteAllOfEntityAsync(provider, "task", progress, ct);
            await DeleteAllOfEntityAsync(provider, "contact", progress, ct);
            await DeleteAllOfEntityAsync(provider, "account", progress, ct);

            _logger.Information("Cleanup completed successfully.");
            progress?.Report("Cleanup completed successfully.");
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
