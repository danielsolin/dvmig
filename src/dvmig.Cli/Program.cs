using System.Text;
using Microsoft.Extensions.DependencyInjection;

using dvmig.Cli.Actions;
using dvmig.Core.Interfaces;
using dvmig.Core.Provisioning;
using dvmig.Core.Settings;
using dvmig.Core.Shared;
using dvmig.Core.Synchronization;

namespace dvmig.Cli
{
   class Program
   {
      private static IServiceProvider? _serviceProvider;
      private static CancellationTokenSource? _currentActionCts;
      private static DateTime _lastCtrlC = DateTime.MinValue;

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
            CliUI.MenuItem choice;

            try
            {
               choice = CliUI.PromptMainMenu(
                  syncActions,
                  maintenanceActions,
                  () => exit = true
               );
            }
            catch(Exception)
            {
               // Handles cases where the prompt is interrupted (e.g., Ctrl+C),
               // preventing a crash and allowing the user to stay in the app.
               if (!CliUI.Confirm("Back (Y) or Quit (N)?", true))
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
               catch(OperationCanceledException)
               {
                  CliUI.WriteWarning("\nOperation interrupted.");

                  if (!CliUI.Confirm("Back (Y) or Quit (N)?", true))
                     exit = true;
               }
               catch(Exception ex)
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
