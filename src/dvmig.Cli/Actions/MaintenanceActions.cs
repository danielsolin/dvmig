using dvmig.Core.Interfaces;
using dvmig.Core.Synchronization;
using Microsoft.Crm.Sdk.Messages;
using Spectre.Console;
using static dvmig.Core.Shared.SystemConstants;

namespace dvmig.Cli.Actions
{
   public class MaintenanceActions(
      ConnectionManager connectionManager,
      ISeedingService seedingService,
      IWipeDataService wipeDataService,
      IPluginService pluginService,
      IValidationService validator,
      ISchemaService schemaService,
      IEntityService entityService,
      ILogger logger,
      ISettingsService settingsService
      ) : BaseActions(
         connectionManager,
         pluginService,
         validator,
         schemaService,
         logger,
         entityService,
         settingsService
      )
   {
      private readonly ISeedingService _seedingService = seedingService;
      private readonly IWipeDataService _wipeDataService = wipeDataService;

      public async Task HandleViewFailuresAsync(CancellationToken ct)
      {
         var target = await ConnectionManager.ConnectAsync(
            ConnectionDirection.Target
         );

         if (target == null)
            return;

         var engine = new SyncEngine(
            null!,
            target,
            null!,
            Logger,
            EntityService,
            new SyncStateService()
         );

         bool isInitialized = await engine.IsFailureLoggingInitializedAsync(
            target,
            ct
         );

         if (!isInitialized)
         {
            CliUI.WriteWarning(
               "Migration failure logging is not initialized on this target."
            );

            AnsiConsole.MarkupLine(
               $"{UiMarkup.Grey}" +
               "Please use 'Install/Update dvmig Components' " +
               "to enable this feature.[/]"
            );

            CliUI.Pause();
            return;
         }

         var failures = await CliUI.RunStatusAsync(
            "Fetching recorded migration failures...",
            async () => await engine.GetFailuresAsync(
               target,
               null,
               ct
            )
         );

         if (failures.Count == 0)
         {
            CliUI.WriteSuccess(
               "No migration failures recorded in Target environment."
            );

            CliUI.Pause();
            return;
         }

         var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Red)
            .Title("[bold red]Migration Failures[/]");

         table.AddColumn("[bold]Entity[/]");
         table.AddColumn("[bold]Source ID[/]");
         table.AddColumn("[bold]Timestamp (UTC)[/]");
         table.AddColumn("[bold]Error Message[/]");

         foreach (var failure in failures)
         {
            table.AddRow(
               failure.EntityLogicalName,
               failure.SourceId,
               failure.TimestampUtc.ToString("yyyy-MM-dd HH:mm:ss"),
               failure.ErrorMessage
            );
         }

         AnsiConsole.Write(table);

         var clearLog = "Would you like to clear the failure log on " +
                        "the target?";

         if (AnsiConsole.Confirm(clearLog, false))
         {
            await CliUI.RunStatusAsync(
               "Clearing failure log...",
               Logger,
               async () => await engine.ClearFailuresAsync(
                  target,
                  ct
               )
            );

            CliUI.WriteSuccess("Failure log cleared.");
         }

         CliUI.Pause();
      }

      public async Task HandleSeedingAsync(CancellationToken ct)
      {
         var provider = await ConnectionManager.ConnectAsync(
            ConnectionDirection.Source
         );

         if (provider == null)
            return;

         var prompt =
            $"How many {UiMarkup.BoldBlue}Accounts[/] " +
            "(with related Contacts and Activities) would you like " +
            "to generate?";

         int count = AnsiConsole.Ask<int>(prompt, 100);

         await CliUI.RunStatusAsync(
            "Seeding data...",
            Logger,
            async () => await _seedingService.SeedSampleDataAsync(
               provider,
               count,
               ct
            )
         );

         CliUI.WriteSuccess("Seeding Finished!");
      }

      public async Task HandleInstallComponentsAsync(CancellationToken ct)
      {
         var provider = await ConnectionManager.ConnectAsync(
            ConnectionDirection.Target
         );

         if (provider == null)
            return;

         await HandleInstallAsync(provider);
         CliUI.Pause();
      }

      public async Task HandleUninstallComponentsAsync(CancellationToken ct)
      {
         var provider = await ConnectionManager.ConnectAsync(
            ConnectionDirection.Target
         );

         if (provider == null)
            return;

         var promptMsg =
            $"{UiMarkup.Red}Are you sure you want " +
            "to remove all dvmig system components (schema and plugins) " +
            "from this environment?[/]";

         if (!AnsiConsole.Confirm(promptMsg, false))
            return;

         try
         {
            await CliUI.RunStatusAsync(
               "Uninstalling components...",
               Logger,
               async () =>
               {
                  Logger.Information("Cleaning target environment...");

                  await PluginService.RemovePluginAsync(provider, ct);

                  await provider.ExecuteAsync(new PublishAllXmlRequest(), ct);

                  await SchemaService.DropSchemaAsync(provider, ct);

                  Logger.Information("Environment cleanup completed.");
               },
               lineByLine: true
            );

            CliUI.WriteSuccess("Uninstallation Finished!");
         }
         catch (OperationCanceledException)
         {
            throw;
         }
         catch (Exception ex)
         {
            var baseEx = ex.GetBaseException();

            CliUI.WriteError(
               "Cleanup failed. " + baseEx.Message
            );
         }

         CliUI.Pause();
      }

      public async Task HandleSourceDataCleanupAsync(CancellationToken ct)
      {
         await HandleDataCleanupAsync(ConnectionDirection.Source, ct);
      }

      public async Task HandleTargetDataCleanupAsync(CancellationToken ct)
      {
         await HandleDataCleanupAsync(ConnectionDirection.Target, ct);
      }

      private async Task HandleDataCleanupAsync(
         ConnectionDirection direction,
         CancellationToken ct
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
               .AddChoices(
                  new[]
                  {
                     "All Recommended Entities",
                     "Select Specific Entities"
                  }
               )
         );

         List<string>? selectedEntities = null;

         if (wipeChoice == "Select Specific Entities")
         {
            selectedEntities = await CliUI.SelectEntitiesAsync(
               EntityService,
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
            var list = SyncSettings.RecommendedEntities
               .Select(e => char.ToUpper(e[0]) + e.Substring(1))
               .ToList();

            var entityNames = list.Count > 1
               ? string.Join(", ", list.Take(list.Count - 1)) +
                 " and " + list.Last()
               : list.FirstOrDefault() ?? string.Empty;

            var warningTable = new Table()
               .Border(TableBorder.None)
               .HideHeaders()
               .AddColumn("Icon")
               .AddColumn("Message");

            warningTable.AddRow(
               "[bold red]![/]",
               $"{UiMarkup.BoldRed}CRITICAL WARNING:[/] This " +
               "operation will delete [bold]EVERY SINGLE[/] " +
               $"{entityNames} record from the {envName} environment."
            );

            warningTable.AddRow(
               "[bold red]![/]",
               $"{UiMarkup.Red}This is NOT restricted to " +
               "test data. Real data will be destroyed.[/]"
            );

            warningTable.AddRow(
               "[bold red]![/]",
               $"{UiMarkup.Red}This action is permanent and " +
               "irreversible.[/]"
            );

            AnsiConsole.Write(
               new Panel(warningTable)
                  .Header("[bold red] DANGER [/]")
                  .BorderColor(Color.Red)
            );
         }

         var wipeText = UiMarkup.WipeDataConfirmation;
         var prompt =
            $"Type {UiMarkup.BoldRed}{wipeText}[/] " +
            "to confirm:";

         var confirmation = AnsiConsole.Ask<string>(prompt);

         if (confirmation != UiMarkup.WipeDataConfirmation)
         {
            CliUI.WriteWarning("Wipe cancelled.");

            return;
         }

         long remainingRecords = -1;
         long initialRecords = -1;
         var startTime = DateTime.Now;

         var progress = new Progress<long>(
            count =>
            {
               remainingRecords = count;
               if (initialRecords == -1)
                  initialRecords = count;
            }
         );

         await AnsiConsole.Status()
            .StartAsync(
               "Initializing wipe...",
               async ctx =>
               {
                  var cleanupTask = _wipeDataService.WipeEntitiesAsync(
                     provider,
                     selectedEntities,
                     progress,
                     ct
                  );

                  while (!cleanupTask.IsCompleted)
                  {
                     if (remainingRecords >= 0)
                     {
                        var elapsed = DateTime.Now - startTime;
                        var deleted = initialRecords - remainingRecords;
                        var etaStr = string.Empty;

                        if (deleted > 0 && elapsed.TotalSeconds > 5)
                        {
                           var recordsPerSec =
                              deleted / elapsed.TotalSeconds;

                           if (recordsPerSec > 0)
                           {
                              var remainingSeconds =
                                 remainingRecords / recordsPerSec;
                              var remainingTime =
                                 TimeSpan.FromSeconds(remainingSeconds);

                              etaStr =
                                 $" [grey]{remainingTime:hh\\:mm\\:ss}[/]";
                           }
                        }

                        ctx.Status(
                           $"[yellow]Wiping data...[/] " +
                           $"{remainingRecords} records remaining..." +
                           etaStr
                        );
                     }

                     await Task.Delay(1000, ct);
                  }

                  await cleanupTask;
               }
            );

         CliUI.WriteSuccess($"Data Wipe Finished for {envName}!");
         CliUI.Pause();
      }
   }
}
