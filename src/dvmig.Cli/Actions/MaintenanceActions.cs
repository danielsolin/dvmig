using dvmig.Core.Interfaces;
using dvmig.Core.Synchronization;
using Spectre.Console;
using static dvmig.Core.Shared.SystemConstants;

namespace dvmig.Cli.Actions
{
   public class MaintenanceActions(
      ConnectionManager connectionManager,
      IEnvironmentService environmentService,
      IEntityService entityService,
      ILogger logger,
      ISettingsService settingsService
   ) : BaseActions(
      connectionManager,
      environmentService,
      logger,
      entityService,
      settingsService
   )
   {
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

         var isInitialized = await engine.IsFailureLoggingInitializedAsync(
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
            table.AddRow(
               failure.EntityLogicalName,
               failure.SourceId,
               failure.TimestampUtc.ToString("yyyy-MM-dd HH:mm:ss"),
               failure.ErrorMessage
            );

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
               async () => await EnvironmentService.UninstallComponentsAsync(
                  provider,
                  ct
               ),
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

            CliUI.WriteError("Cleanup failed. " + baseEx.Message);
         }

         CliUI.Pause();
      }
   }
}
