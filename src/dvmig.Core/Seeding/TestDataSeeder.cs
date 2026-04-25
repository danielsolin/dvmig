using Bogus;
using dvmig.Providers;
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

        /// <summary>
        /// Initializes a new instance of the <see cref="TestDataSeeder"/> class.
        /// </summary>
        /// <param name="logger">The logger instance.</param>
        public TestDataSeeder(ILogger logger)
        {
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task SeedTestDataAsync(
            IDataverseProvider provider,
            int count,
            IProgress<string>? progress = null,
            CancellationToken ct = default
        )
        {
            _logger.Information("Starting test data seeding: {Count} records", count);
            progress?.Report($"Generating {count} accounts and contacts...");

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

            var accounts = accountFaker.Generate(count);
            var contacts = contactFaker.Generate(count);

            int current = 0;
            int total = count * 2;

            progress?.Report("Inserting Accounts...");
            foreach (var account in accounts)
            {
                ct.ThrowIfCancellationRequested();
                await provider.CreateAsync(account, ct);
                current++;
                if (current % 10 == 0 || current == total)
                {
                    progress?.Report($"Seeding progress: {current}/{total} records");
                }
            }

            progress?.Report("Inserting Contacts...");
            foreach (var contact in contacts)
            {
                ct.ThrowIfCancellationRequested();
                await provider.CreateAsync(contact, ct);
                current++;
                if (current % 10 == 0 || current == total)
                {
                    progress?.Report($"Seeding progress: {current}/{total} records");
                }
            }

            _logger.Information("Seeding completed successfully.");
            progress?.Report("Seeding completed successfully.");
        }
    }
}
