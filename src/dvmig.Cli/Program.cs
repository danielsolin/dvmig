using System.Runtime.Versioning;
using System.Text;
using dvmig.Cli.Actions;
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
      private record MenuItem(string Label, Func<CancellationToken, Task>? Action);

      private static CancellationTokenSource? _currentActionCts;
      private static DateTime _lastCtrlC = DateTime.MinValue;

      static async Task Main(string[] args)
      {
         Console.OutputEncoding = Encoding.UTF8;

         // Set up global Ctrl+C handling
         Console.CancelKeyPress += (s, e) =>
         {
            e.Cancel = true;

            var now = DateTime.Now;
            if (now - _lastCtrlC < TimeSpan.FromSeconds(1))
            {
               // Force immediate exit on double-tap
               Environment.Exit(0);
            }

            _lastCtrlC = now;

            // Signal the current action to stop
            _currentActionCts?.Cancel();
         };

         var logger = new dvmig.Core.Shared.Logger();
         var retryService = new RetryService(logger);

         var settingsService = new SettingsService();
         var seedingService = new SeedingService(logger, retryService);
         var entityService = new EntityService(logger);
         var metadataService = new MetadataService(logger);
         var reconciliationService = new ReconciliationService(entityService, logger);
         var validationService = new ValidationService();
         var schemaService = new SchemaService(logger);
         var sourceDateService = new SourceDateService(logger);
         var pluginService = new PluginService(logger);

         var connectionManager = new ConnectionManager(settingsService);

         var migrationActions = new MigrationActions(
            connectionManager,
            metadataService,
            pluginService,
            sourceDateService,
            validationService,
            schemaService,
            logger
         );

         var reconciliationActions = new ReconciliationActions(
            connectionManager,
            reconciliationService,
            metadataService,
            pluginService,
            sourceDateService,
            validationService,
            schemaService,
            logger
         );

         var maintenanceActions = new MaintenanceActions(
            connectionManager,
            seedingService,
            pluginService,
            sourceDateService,
            validationService,
            schemaService,
            metadataService,
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

            MenuItem choice;
            try
            {
               choice = AnsiConsole.Prompt(prompt);
            }
            catch (Exception)
            {
               // Catch potential Spectre.Console exceptions during interrupt
               if (!AnsiConsole.Confirm("Back (Y) or Quit (N)?", true))
                  exit = true;

               continue;
            }

            if (choice.Action != null)
            {
               _currentActionCts = new CancellationTokenSource();

               try
               {
                  await choice.Action(_currentActionCts.Token);
               }
               catch (OperationCanceledException)
               {
                  AnsiConsole.MarkupLine(
                     $"\n{SystemConstants.UiMarkup.Yellow}Operation interrupted.[/]"
                  );

                  if (!AnsiConsole.Confirm("Back (Y) or Quit (N)?", true))
                     exit = true;
               }
               catch (Exception ex)
               {
                  CliUI.WriteError($"An unexpected error occurred: {ex.Message}");
               }
               finally
               {
                  _currentActionCts.Dispose();
                  _currentActionCts = null;
               }
            }

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
               $"Migrate Recommended {SystemConstants.UiMarkup.Grey}" +
               "(Accounts, Contacts, Activities)[/]",
               migrationActions.HandleRecommendedSyncAsync
            ),
            new MenuItem(
               $"Find & Fix Recommended {SystemConstants.UiMarkup.Grey}" +
               "(Accounts, Contacts, Activities)[/]",
               reconciliationActions.HandleRecommendedReconciliationAsync
            ),
            new MenuItem(
               $"Migrate Selected {SystemConstants.UiMarkup.Grey}" +
               "(pick entities)[/]",
               migrationActions.HandleMigrationAsync
            ),
            new MenuItem(
               $"Find & Fix Selected {SystemConstants.UiMarkup.Grey}" +
               "(pick entities)[/]",
               reconciliationActions.HandlePerformReconciliationAsync
            )
         };

         prompt.AddChoiceGroup(
            new MenuItem(
               $"🚀 {SystemConstants.UiMarkup.BoldGreen}Synchronization[/]",
               null
            ),
            syncGroup
         );

         if (developerMode)
         {
            var maintenanceGroup = new List<MenuItem>
            {
               new MenuItem(
                  $"Install DVMig Components {SystemConstants.UiMarkup.Grey}" +
                  "(Target)[/]",
                  maintenanceActions.HandleInstallMenuAsync
               ),
               new MenuItem(
                  $"Uninstall DVMig Components {SystemConstants.UiMarkup.Grey}" +
                  "(Target)[/]",
                  maintenanceActions.HandleTargetComponentsCleanupAsync
               ),
               new MenuItem(
                  "View Recorded Migration Failures",
                  reconciliationActions.HandleViewFailuresAsync
               ),
            };

            prompt.AddChoiceGroup(
               new MenuItem(
                  $"🛠️ {SystemConstants.UiMarkup.BoldCyan}Maintenance[/]",
                  null
               ),
               maintenanceGroup
            );

            var dataGroup = new List<MenuItem>
            {
               new MenuItem(
                  $"Generate Sample Data {SystemConstants.UiMarkup.Grey}" +
                  "(Source)[/]",
                  maintenanceActions.HandleSeedingAsync
               ),
               new MenuItem(
                  $"Wipe Data on Source {SystemConstants.UiMarkup.Grey}" +
                  "(Caution!)[/]",
                  maintenanceActions.HandleSourceDataCleanupAsync
               ),
               new MenuItem(
                  $"Wipe Data on Target {SystemConstants.UiMarkup.Grey}" +
                  "(Caution!)[/]",
                  maintenanceActions.HandleTargetDataCleanupAsync
               )
            };

            prompt.AddChoiceGroup(
               new MenuItem(
                  $"🧪 {SystemConstants.UiMarkup.BoldMagenta}Data Management[/]",
                  null
               ),
               dataGroup
            );
         }

         prompt.AddChoices(new[]
         {
            new MenuItem(
               "Exit",
               (ct) =>
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
