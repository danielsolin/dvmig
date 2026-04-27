using System.Runtime.Versioning;
using dvmig.Cli.Actions;
using dvmig.Cli.Infrastructure;
using dvmig.Core.Logging;
using dvmig.Core.Metadata;
using dvmig.Core.Provisioning;
using dvmig.Core.Seeding;
using dvmig.Core.Settings;
using dvmig.Core.Synchronization;
using Spectre.Console;

namespace dvmig.Cli
{
    /// <summary>
    /// Entry point for the Terminal User Interface (TUI) version of the
    /// Dataverse Migrator.
    /// </summary>
    [SupportedOSPlatform("windows")]
    class Program
    {
        private record MenuItem(string Label, Func<Task> Action);

        static async Task Main(string[] args)
        {
            var logger = LoggerInitializer.Initialize("dvmig.Cli");
            var retryStrategy = new RetryStrategy(logger);

            var settingsService = new SettingsService();
            var stateTracker = new LocalFileStateTracker();
            var seeder = new TestDataSeeder(logger, retryStrategy);
            var metadataService = new MetadataService();
            var reconciliationService = new ReconciliationService();
            var setupService = new SetupService(
                new EnvironmentValidator(),
                new SchemaManager(logger),
                new PluginDeployer(logger),
                logger
            );

            var connectionManager = new ConnectionManager(settingsService);

            var migrationActions = new MigrationActions(
                connectionManager,
                metadataService,
                setupService,
                stateTracker,
                logger
            );

            var reconciliationActions = new ReconciliationActions(
                connectionManager,
                reconciliationService,
                logger
            );

            var maintenanceActions = new MaintenanceActions(
                connectionManager,
                seeder,
                setupService,
                logger
            );

            bool enableSourceCleanup = args.Contains("--enable-source-cleanup");
            bool enableTargetCleanup = args.Contains("--enable-target-cleanup");
            CliUI.WriteHeader();

            bool exit = false;
            while (!exit)
            {
                var menu = GetMenu(
                    enableSourceCleanup,
                    enableTargetCleanup,
                    migrationActions,
                    reconciliationActions,
                    maintenanceActions,
                    () => exit = true
                );

                var choice = AnsiConsole.Prompt(
                    new SelectionPrompt<MenuItem>()
                        .Title("What would you like to do?")
                        .PageSize(10)
                        .UseConverter(m => m.Label)
                        .AddChoices(menu)
                );

                await choice.Action();

                if (!exit)
                {
                    CliUI.Pause();
                    CliUI.WriteHeader();
                }
            }
        }

        private static List<MenuItem> GetMenu(
            bool enableSourceCleanup,
            bool enableTargetCleanup,
            MigrationActions migrationActions,
            ReconciliationActions reconciliationActions,
            MaintenanceActions maintenanceActions,
            Action onExit
        )
        {
            var menu = new List<MenuItem>
            {
                new MenuItem(
                    "Recommended Sync (Accounts, Contacts, Activities)",
                    migrationActions.HandleRecommendedSyncAsync
                ),
                new MenuItem(
                    "Migrate Data (Custom Selection)",
                    migrationActions.HandleMigrationAsync
                ),
                new MenuItem(
                    "Reconcile Migration / View Failures",
                    reconciliationActions.HandleReconcileAsync
                ),
                new MenuItem(
                    "Seed Test Data",
                    maintenanceActions.HandleSeedingAsync
                ),
                new MenuItem(
                    "Install/Update dvmig Components on Target",
                    maintenanceActions.HandleInstallAsync
                ),
                new MenuItem(
                    "Uninstall dvmig Components from Target",
                    maintenanceActions.HandleTargetComponentsCleanupAsync
                )
            };

            if (enableSourceCleanup)
                menu.Add(new MenuItem(
                    "Wipe SOURCE: Delete ALL Accounts, Contacts, and " +
                    "Activities (DANGEROUS!)",
                    maintenanceActions.HandleSourceDataCleanupAsync
                ));

            if (enableTargetCleanup)
                menu.Add(new MenuItem(
                    "Wipe TARGET: Delete ALL Accounts, Contacts, and " +
                    "Activities (DANGEROUS!)",
                    maintenanceActions.HandleTargetDataCleanupAsync
                ));

            menu.Add(new MenuItem("Exit", () =>
            {
                onExit();

                return Task.CompletedTask;
            }));

            return menu;
        }
    }
}
