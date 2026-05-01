using dvmig.Core.Interfaces;
using dvmig.Core.Shared;
using Spectre.Console;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Crm.Sdk.Messages;

namespace dvmig.Cli.Actions
{
   public class MaintenanceActions : BaseActions
   {
      private readonly ITestDataSeeder _seeder;
      private readonly IMetadataService _metadataService;

      public MaintenanceActions(
         ConnectionManager connectionManager,
         ITestDataSeeder seeder,
         IPluginService pluginService,
         ISourceDateService sourceDateService,
         IEnvironmentValidator validator,
         ISchemaService schemaService,
         IMetadataService metadataService,
         ISyncStateTracker stateTracker,
         ILogger logger
      ) : base(connectionManager, pluginService, sourceDateService, validator, schemaService, stateTracker, logger)
      {
         _seeder = seeder;
         _metadataService = metadataService;
      }

      public async Task HandleSeedingAsync()
      {
         var provider = await ConnectionManager.ConnectAsync(
            ConnectionDirection.Source
         );

         if (provider == null)
            return;

         var prompt = $"How many {SystemConstants.UiMarkup.BoldBlue}Accounts[/] would you like " +
                      "to generate?";

         int count = AnsiConsole.Ask<int>(prompt, 100);

         await CliUI.RunStatusAsync("Seeding data...", Logger, async () =>
               await _seeder.SeedTestDataAsync(
                  provider,
                  count)
         );

         CliUI.WriteSuccess("Seeding Finished!");
      }

      public async Task HandleInstallMenuAsync()
      {
         var provider = await ConnectionManager.ConnectAsync(
            ConnectionDirection.Target
         );

         if (provider == null)
            return;

         await HandleInstallAsync(provider);
      }

      public async Task HandleTargetComponentsCleanupAsync()
      {
         var provider = await ConnectionManager.ConnectAsync(
            ConnectionDirection.Target
         );

         if (provider == null)
            return;

         var promptMsg = $"{SystemConstants.UiMarkup.Red}Are you sure you want to remove all dvmig " +
                         "system components (schema and plugins) from " +
                         "this environment?[/]";

         if (!AnsiConsole.Confirm(promptMsg, false))
            return;

         try
         {
            await CliUI.RunStatusAsync("Uninstalling components...", Logger, async () =>
               {
                  Logger.Information("Cleaning target environment...");

                  // 1. Remove Plugin
                  await PluginService.RemovePluginAsync(provider);

                  // Ensure plugin changes are published before schema removal
                  await provider.ExecuteAsync(new PublishAllXmlRequest());

                  // 2. Drop Schema
                  await SchemaService.DropSchemaAsync(provider);

                  Logger.Information("Environment cleanup completed.");
               }
            );

            CliUI.WriteSuccess("Uninstallation Finished!");
         }
         catch (Exception ex)
         {
            var baseEx = ex.GetBaseException();

            CliUI.WriteError(
               "Cleanup failed. " + baseEx.Message
            );
         }
      }

      public async Task HandleSourceDataCleanupAsync()
      {
         await HandleDataCleanupInternalAsync(ConnectionDirection.Source);
      }

      public async Task HandleTargetDataCleanupAsync()
      {
         await HandleDataCleanupInternalAsync(ConnectionDirection.Target);
      }

      private async Task HandleDataCleanupInternalAsync(
         ConnectionDirection direction
      )
      {
         var provider = await ConnectionManager.ConnectAsync(direction);

         if (provider == null)
            return;

         var envName = direction == ConnectionDirection.Source
            ? "SOURCE"
            : "TARGET";

         var wipeChoice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
               .Title($"What data do you want to wipe on {envName}?")
               .AddChoices(new[]
               {
                  "All Recommended Entities (Account, Contact, Activities)",
                  "Select Specific Entities"
               })
         );

         List<string>? selectedEntities = null;

         if (wipeChoice == "Select Specific Entities")
         {
            selectedEntities = await CliUI.SelectEntitiesAsync(
               _metadataService,
               provider
            );

            if (selectedEntities == null || selectedEntities.Count == 0)
            {
               CliUI.WriteWarning("No entities selected. Wipe cancelled.");

               return;
            }
         }
         else
         {
            AnsiConsole.MarkupLine(
               $"{SystemConstants.UiMarkup.BoldRed}CRITICAL WARNING:[/] This operation will delete " +
               $"[bold]EVERY SINGLE[/] Account, Contact, Task, Phone Call, " +
               $"and Email record from the {envName} environment."
            );
         }

         AnsiConsole.MarkupLine(
            $"{SystemConstants.UiMarkup.Red}This is NOT restricted to test data. Real data will " +
            "be destroyed.[/]"
         );
         AnsiConsole.MarkupLine(
            $"{SystemConstants.UiMarkup.Red}This action is permanent and irreversible.[/]"
         );

         var wipeText = SystemConstants.UiMarkup.WipeDataConfirmation;
         var prompt = $"Type {SystemConstants.UiMarkup.BoldRed}{wipeText}[/] to confirm:";
         var confirmation = AnsiConsole.Ask<string>(prompt);

         if (confirmation != SystemConstants.UiMarkup.WipeDataConfirmation)
         {
            CliUI.WriteWarning("Wipe cancelled.");

            return;
         }

         await CliUI.RunStatusAsync("Wiping data...", Logger, async () =>
               await _seeder.CleanTestDataAsync(
                  provider,
                  selectedEntities)
         );

         CliUI.WriteSuccess($"Data Wipe Finished for {envName}!");
      }
   }
}
