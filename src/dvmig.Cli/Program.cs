using System.Runtime.Versioning;
using System.Text;

using dvmig.Cli.Actions;
using dvmig.Core.Provisioning;
using dvmig.Core.Settings;
using dvmig.Core.Shared;
using dvmig.Core.Synchronization;

using Spectre.Console;

namespace dvmig.Cli
{
   [SupportedOSPlatform("windows")]
   class Program
   {
      private static SyncActions? _migrationActions;
      private static MaintenanceActions? _maintenanceActions;

      private static CancellationTokenSource? _currentActionCts;
      private static DateTime _lastCtrlC = DateTime.MinValue;
      private static bool _developerMode;

      private record MenuItem(
         string Label,
         Func<CancellationToken, Task>? Action
      );


      static async Task Main(string[] args)
      {
         Init(args);
         InitConsole();
         await HandleMenuActions();
      }

      private static void Init(string[] args)
      {
         var logger = new Logger();
         var retryService = new RetryService(logger);

         var settingsService = new SettingsService();
         var seedingService = new SeedingService(logger, retryService);
         var wipeDataService = new WipeDataService(logger, retryService);
         var entityService = new EntityService(logger);
         var metadataService = new MetadataService(logger);

         var validationService = new ValidationService();
         var schemaService = new SchemaService(logger);
         var sourceDataService = new SourceDataService(logger);
         var pluginService = new PluginService(logger);

         var connectionManager = new ConnectionManager(settingsService);

         // We use the same connection for failure logging as the target
         var failureService = new FailureService(
            null!, // Will be set by ConnectionManager during sync
            logger
         );

         _migrationActions = new SyncActions(
            connectionManager,
            metadataService,
            pluginService,
            sourceDataService,
            validationService,
            schemaService,
            logger
         );

         _maintenanceActions = new MaintenanceActions(
            connectionManager,
            seedingService,
            wipeDataService,
            pluginService,
            sourceDataService,
            validationService,
            schemaService,
            metadataService,
            failureService,
            logger
         );

         _developerMode =
            args.Contains(SystemConstants.CliSettings.DevShort) ||
            args.Contains(SystemConstants.CliSettings.DevLong) ||
            args.Contains(SystemConstants.CliSettings.DevFull);
      }

      private static void InitConsole()
      {
         Console.OutputEncoding = Encoding.UTF8;

         Console.CancelKeyPress += (s, e) =>
         {
            e.Cancel = true;

            var now = DateTime.Now;

            if (now - _lastCtrlC < TimeSpan.FromSeconds(1))
               Environment.Exit(0);

            _lastCtrlC = now;

            _currentActionCts?.Cancel();
         };
      }

      private static SelectionPrompt<MenuItem> GetMenu(
         bool developerMode,
         SyncActions syncActions,
         MaintenanceActions maintenanceActions,
         Action onExit
      )
      {
         var prompt = new SelectionPrompt<MenuItem>()
            .Title("What would you like to do?")
            .PageSize(15)
            .UseConverter(m => m.Label)
            .HighlightStyle(new Style
            {
               Foreground = Color.MediumOrchid
            });

         var syncGroup = new List<MenuItem>
         {
            new MenuItem(
               $"Sync Recommended {SystemConstants.UiMarkup.Grey}" +
               "(Accounts, Contacts, Activities)[/]",
               ct => syncActions.HandleRecommendedSyncAsync(ct, false)
            ),
            new MenuItem(
               $"Re-sync Recommended {SystemConstants.UiMarkup.Grey}" +
               "(Force update all records)[/]",
               ct => syncActions.HandleRecommendedSyncAsync(ct, true)
            ),
            new MenuItem(
               $"Sync Selected {SystemConstants.UiMarkup.Grey}" +
               "(pick entities)[/]",
               ct => syncActions.HandleSelectedSyncAsync(ct, false)
            ),
            new MenuItem(
               $"Re-sync Selected {SystemConstants.UiMarkup.Grey}" +
               "(Force update pick entities)[/]",
               ct => syncActions.HandleSelectedSyncAsync(ct, true)
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
                  $"Install DVMig Components {SystemConstants.UiMarkup.Grey}"
                  + "(Target)[/]",
                  maintenanceActions.HandleInstallMenuAsync
               ),
               new MenuItem(
                  $"Uninstall DVMig Components {SystemConstants.UiMarkup.Grey}"
                  + "(Target)[/]",
                  maintenanceActions.HandleTargetComponentsCleanupAsync
               ),
               new MenuItem(
                  "View Recorded Migration Failures",
                  maintenanceActions.HandleViewFailuresAsync
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
                  $"🧪 {SystemConstants.UiMarkup.BoldMagenta}"
                  + "Data Management[/]",
                  null
               ),
               dataGroup
            );
         }

         prompt.AddChoices(
            new[]
            {
               new MenuItem(
                  "Exit",
                  (ct) =>
                  {
                     onExit();

                     return Task.CompletedTask;
                  }
               )
            }
         );

         return prompt;
      }

      private static async Task HandleMenuActions()
      {
         CliUI.WriteHeader();

         bool exit = false;

         while (!exit)
         {
            var prompt = GetMenu(
               _developerMode,
               _migrationActions!,
               _maintenanceActions!,
               () => exit = true
            );

            MenuItem choice;

            try
            {
               choice = AnsiConsole.Prompt(prompt);
            }
            catch (Exception)
            {
               // Handles cases where the prompt is interrupted (e.g., Ctrl+C),
               // preventing a crash and allowing the user to stay in the app.
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
                     $"\n{SystemConstants.UiMarkup.Yellow}"
                     + "Operation interrupted.[/]"
                  );

                  if (!AnsiConsole.Confirm("Back (Y) or Quit (N)?", true))
                     exit = true;
               }
               catch (Exception ex)
               {
                  CliUI.WriteError($"An unexpected error occurred: "
                     + $"{ex.Message}");
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
   }
}
