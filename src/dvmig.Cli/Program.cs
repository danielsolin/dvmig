using System.Runtime.Versioning;
using dvmig.Cli.Actions;
using dvmig.Cli.Infrastructure;
using dvmig.Core.Logging;
using dvmig.Core.Metadata;
using dvmig.Core.Provisioning;
using dvmig.Core.Seeding;
using dvmig.Core.Shared;
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
            metadataService,
            setupService,
            stateTracker,
            logger
         );

         var maintenanceActions = new MaintenanceActions(
             connectionManager,
             seeder,
             setupService,
             metadataService,
             logger
         );
         bool devoplerMode =
            args.Contains(SystemConstants.CliSettings.DevShort) ||
            args.Contains(SystemConstants.CliSettings.DevLong) ||
            args.Contains(SystemConstants.CliSettings.DevFull);

         CliUI.WriteHeader();

         bool exit = false;

         while (!exit)
         {
            var menu = GetMenu(
               devoplerMode,
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
         bool developerMode,
         MigrationActions migrationActions,
         ReconciliationActions reconciliationActions,
         MaintenanceActions maintenanceActions,
         Action onExit
      )
      {
         var menu = new List<MenuItem>
         {
            new MenuItem(
               "SYNC: Recommended (Accounts, Contacts, Activities)",
               migrationActions.HandleRecommendedSyncAsync
            ),
            new MenuItem(
               "SYNC: Custom (Select Entities)",
               migrationActions.HandleMigrationAsync
            )
         };

         if (developerMode)
         {
            menu.Add(
               new MenuItem(
                  "RECON: View Recorded Failures",
                  reconciliationActions.HandleViewFailuresAsync
               )
            );

            menu.Add(
               new MenuItem(
                  "RECON: Reconcile Data (Fix Discrepancies)",
                  reconciliationActions.HandlePerformReconciliationAsync
               )
            );

            menu.Add(
               new MenuItem(
                  "COMP: Install DVMig Components on Target",
                  maintenanceActions.HandleInstallAsync
               )
            );

            menu.Add(
               new MenuItem(
                  "COMP: Uninstall DVMig Components from Target",
                  maintenanceActions.HandleTargetComponentsCleanupAsync
               )
            );

            menu.Add(
               new MenuItem(
                  "DATA: Generate Sample Data on Source",
                  maintenanceActions.HandleSeedingAsync
               )
            );

            menu.Add(
               new MenuItem(
                  "DATA: Wipe Data on Source (Use with caution!)",
                  maintenanceActions.HandleSourceDataCleanupAsync
               )
            );

            menu.Add(
               new MenuItem(
                  "DATA: Wipe Data on Target (Use with caution!)",
                  maintenanceActions.HandleTargetDataCleanupAsync
               )
            );
         }

         menu.Add(
            new MenuItem(
               "Exit",
               () =>
               {
                  onExit();

                  return Task.CompletedTask;
               }
            )
         );

         return menu;
      }
   }
}
