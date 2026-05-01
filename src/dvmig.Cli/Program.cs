using System.Runtime.Versioning;
using System.Text;
using dvmig.Cli.Actions;
using dvmig.Core.Logging;
using dvmig.Core.Provisioning;
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
      private record MenuItem(string Label, Func<Task>? Action);

      static async Task Main(string[] args)
      {
         Console.OutputEncoding = Encoding.UTF8;

         var logger = LoggerInitializer.Initialize("dvmig.Cli");
         var retryStrategy = new RetryStrategy(logger);

         var settingsService = new SettingsService();
         var stateTracker = new LocalFileStateTracker();
         var seeder = new TestDataSeeder(logger, retryStrategy);
         var metadataService = new MetadataService();
         var reconciliationService = new ReconciliationService();
         var validator = new EnvironmentValidator();
         var schemaManager = new SchemaManager(logger);
         var sourceDateService = new SourceDateService(logger);
         var setupService = new SetupService(
            validator,
            schemaManager,
            new PluginDeployer(logger),
            logger
         );

         var connectionManager = new ConnectionManager(settingsService);

         var migrationActions = new MigrationActions(
            connectionManager,
            metadataService,
            setupService,
            sourceDateService,
            validator,
            schemaManager,
            stateTracker,
            logger
         );

         var reconciliationActions = new ReconciliationActions(
            connectionManager,
            reconciliationService,
            metadataService,
            setupService,
            sourceDateService,
            validator,
            schemaManager,
            stateTracker,
            logger
         );

         var maintenanceActions = new MaintenanceActions(
             connectionManager,
             seeder,
             setupService,
             sourceDateService,
             validator,
             schemaManager,
             metadataService,
             stateTracker,
             logger
         );
         bool developerMode =
            args.Contains(SystemConstants.CliSettings.DevShort) ||
            args.Contains(SystemConstants.CliSettings.DevLong) ||
            args.Contains(SystemConstants.CliSettings.DevFull);

         CliUI.WriteHeader();

         bool exit = false;

         while (!exit)
         {
            var prompt = GetMenu(
               developerMode,
               migrationActions,
               reconciliationActions,
               maintenanceActions,
               () => exit = true
            );

            var choice = AnsiConsole.Prompt(prompt);

            if (choice.Action != null)
               await choice.Action();

            if (!exit)
            {
               CliUI.Pause();
               CliUI.WriteHeader();
            }
         }
      }

      private static SelectionPrompt<MenuItem> GetMenu(
         bool developerMode,
         MigrationActions migrationActions,
         ReconciliationActions reconciliationActions,
         MaintenanceActions maintenanceActions,
         Action onExit
      )
      {
         var prompt = new SelectionPrompt<MenuItem>()
            .Title("What would you like to do?")
            .PageSize(15)
            .UseConverter(m => m.Label);

         var syncGroup = new List<MenuItem>
         {
            new MenuItem(
               $"Migrate Recommended {SystemConstants.UiMarkup.Grey}(Accounts, Contacts, " +
               "Activities)[/]",
               migrationActions.HandleRecommendedSyncAsync
            ),
            new MenuItem(
               $"Find & Fix Recommended {SystemConstants.UiMarkup.Grey}(Accounts, Contacts, Activities)[/]",
               reconciliationActions.HandleRecommendedReconciliationAsync
            ),
            new MenuItem(
               $"Migrate Selected {SystemConstants.UiMarkup.Grey}(pick entities)[/]",
               migrationActions.HandleMigrationAsync
            ),
            new MenuItem(
               $"Find & Fix Selected {SystemConstants.UiMarkup.Grey}(pick entities)[/]",
               reconciliationActions.HandlePerformReconciliationAsync
            )
         };

         prompt.AddChoiceGroup(
            new MenuItem($"🚀 {SystemConstants.UiMarkup.BoldGreen}Synchronization[/]", null),
            syncGroup
         );

         if (developerMode)
         {
            var maintenanceGroup = new List<MenuItem>
            {
               new MenuItem(
                  $"Install DVMig Components {SystemConstants.UiMarkup.Grey}(Target)[/]",
                  maintenanceActions.HandleInstallMenuAsync
               ),
               new MenuItem(
                  $"Uninstall DVMig Components {SystemConstants.UiMarkup.Grey}(Target)[/]",
                  maintenanceActions.HandleTargetComponentsCleanupAsync
               ),
               new MenuItem(
                  "View Recorded Migration Failures",
                  reconciliationActions.HandleViewFailuresAsync
               ),
            };

            prompt.AddChoiceGroup(
               new MenuItem($"🛠️ {SystemConstants.UiMarkup.BoldCyan}Maintenance[/]", null),
               maintenanceGroup
            );

            var dataGroup = new List<MenuItem>
            {
               new MenuItem(
                  $"Generate Sample Data {SystemConstants.UiMarkup.Grey}(Source)[/]",
                  maintenanceActions.HandleSeedingAsync
               ),
               new MenuItem(
                  $"Wipe Data on Source {SystemConstants.UiMarkup.Grey}(Caution!)[/]",
                  maintenanceActions.HandleSourceDataCleanupAsync
               ),
               new MenuItem(
                  $"Wipe Data on Target {SystemConstants.UiMarkup.Grey}(Caution!)[/]",
                  maintenanceActions.HandleTargetDataCleanupAsync
               )
            };

            prompt.AddChoiceGroup(
               new MenuItem($"🧪 {SystemConstants.UiMarkup.BoldMagenta}Data Management[/]", null),
               dataGroup
            );
         }

         prompt.AddChoices(new[]
         {
            new MenuItem(
               "Exit",
               () =>
               {
                  onExit();

                  return Task.CompletedTask;
               }
            )
         });

         return prompt;
      }
   }
}
