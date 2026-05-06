using System.Runtime.Versioning;
using System.Text;

using dvmig.Cli.Actions;
using dvmig.Core.Interfaces;
using dvmig.Core.Provisioning;
using dvmig.Core.Settings;
using dvmig.Core.Shared;
using dvmig.Core.Synchronization;
using Microsoft.Extensions.DependencyInjection;

using Spectre.Console;

namespace dvmig.Cli
{
   [SupportedOSPlatform("windows")]
   class Program
   {
      private static IServiceProvider? _serviceProvider;
      private static CancellationTokenSource? _currentActionCts;
      private static DateTime _lastCtrlC = DateTime.MinValue;
      private static bool _developerMode;

      private record MenuItem(
         string Label,
         Func<CancellationToken, Task>? Action
      );

      static async Task Main(string[] args)
      {
         InitDI();
         InitConsole(args);
         await HandleMenuActions();
      }

      private static void InitDI()
      {
         var services = new ServiceCollection();

         // Shared Infrastructure
         services.AddSingleton<ILogger, Logger>();
         services.AddSingleton<ISettingsService, SettingsService>();
         services.AddSingleton<ConnectionManager>();
         services.AddSingleton<ISyncStateService, SyncStateService>();

         // Provisioning & Metadata
         services.AddTransient<IUserService, UserService>();
         services.AddTransient<ISeedingService, SeedingService>();
         services.AddTransient<IWipeDataService, WipeDataService>();
         services.AddTransient<IEntityService, EntityService>();

         services.AddTransient<IValidationService, ValidationService>();
         services.AddTransient<ISchemaService, SchemaService>();
         services.AddTransient<IPluginService, PluginService>();

         // CLI Actions
         services.AddTransient<SyncActions>();
         services.AddTransient<MaintenanceActions>();

         _serviceProvider = services.BuildServiceProvider();
      }

      private static void InitConsole(string[] args)
      {
         _developerMode =
            args.Contains(SystemConstants.CliSettings.DevShort) ||
            args.Contains(SystemConstants.CliSettings.DevLong) ||
            args.Contains(SystemConstants.CliSettings.DevFull);

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
               $"Sync Recommended",
               ct => syncActions.HandleRecommendedSyncAsync(ct, false)
            ),            
            new MenuItem(
               $"Sync Selected {SystemConstants.UiMarkup.Grey}" +
               "(pick entities)[/]",
               ct => syncActions.HandleSelectedSyncAsync(ct, false)
            ),
            new MenuItem(
               $"Re-sync Recommended",
               ct => syncActions.HandleRecommendedSyncAsync(ct, true)
            ),
            new MenuItem(
               $"Re-sync Selected {SystemConstants.UiMarkup.Grey}" +
               "(pick entities)[/]",
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

         if (_serviceProvider == null)
         {
            CliUI.WriteError("Service provider is not initialized.");
            return;
         }

         var syncActions = _serviceProvider.GetRequiredService<SyncActions>();
         var maintenanceActions = _serviceProvider
            .GetRequiredService<MaintenanceActions>();

         bool exit = false;

         while (!exit)
         {
            var prompt = GetMenu(
               _developerMode,
               syncActions,
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
