using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using dvmig.Core.Interfaces;
using dvmig.Core.Shared;
using dvmig.Core.Synchronization;
using Spectre.Console;

namespace dvmig.Cli.Actions
{
   public class ReconciliationActions : BaseActions
   {
      private readonly IReconciliationService _reconciliationService;
      private readonly IMetadataService _metadataService;

      public ReconciliationActions(
         ConnectionManager connectionManager,
         IReconciliationService reconciliationService,
         IMetadataService metadataService,
         IPluginService pluginService,
         ISourceDateService sourceDateService,
         IValidationService validator,
         ISchemaService schemaService,
         ILogger logger
      ) : base(
         connectionManager,
         pluginService,
         sourceDateService,
         validator,
         schemaService,
         logger
      )
      {
         _reconciliationService = reconciliationService;
         _metadataService = metadataService;
      }

      public async Task HandleViewFailuresAsync(CancellationToken ct)
      {
         var target = await ConnectionManager.ConnectAsync(
            ConnectionDirection.Target
         );

         if (target == null)
            return;

         bool isInitialized = await _reconciliationService.IsInitializedAsync(
            target,
            ct
         );

         if (!isInitialized)
         {
            CliUI.WriteWarning(
               "Migration failure logging is not initialized on this target."
            );

            AnsiConsole.MarkupLine(
               $"{SystemConstants.UiMarkup.Grey}" +
               "Please use 'Install/Update dvmig Components' " +
               "to enable this feature.[/]"
            );

            return;
         }

         var failures = await CliUI.RunStatusAsync(
            "Fetching recorded migration failures...",
            async () => await _reconciliationService.GetFailuresAsync(
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

            return;
         }

         var table = new Table();
         table.AddColumn("Entity");
         table.AddColumn("Source ID");
         table.AddColumn("Timestamp (UTC)");
         table.AddColumn("Error Message");

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
               async () => await _reconciliationService.ClearFailuresAsync(
                  target,
                  ct
               )
            );

            CliUI.WriteSuccess("Failure log cleared.");
         }
      }

      public async Task HandleRecommendedReconciliationAsync(CancellationToken ct)
      {
         var (source, target, engine) = await SetupSyncEngineAsync();

         if (source == null || target == null || engine == null)
            return;

         var recommendedEntities = SystemConstants.SyncSettings
            .RecommendedEntities;

         AnsiConsole.MarkupLine(
            $"{SystemConstants.UiMarkup.BoldCyan}Recommended " +
            "Reconciliation Order:[/]"
         );

         foreach (var entity in recommendedEntities)
            AnsiConsole.MarkupLine($" - {entity}");

         if (!AnsiConsole.Confirm(
            "Proceed with this reconciliation plan?",
            true
         ))
         {
            CliUI.WriteWarning("Recommended reconciliation cancelled.");

            return;
         }

         await RunReconciliationAsync(
            source,
            target,
            engine,
            recommendedEntities.ToList(),
            ct
         );

         CliUI.WriteSuccess("Recommended Reconciliation Finished!");
      }

      public async Task HandlePerformReconciliationAsync(CancellationToken ct)
      {
         var (source, target, engine) = await SetupSyncEngineAsync();

         if (source == null || target == null || engine == null)
            return;

         var selectedEntities = await CliUI.SelectEntitiesAsync(
            _metadataService,
            source
         );

         if (selectedEntities == null || selectedEntities.Count == 0)
         {
            CliUI.WriteWarning("No entities selected for reconciliation.");

            return;
         }

         await RunReconciliationAsync(
            source,
            target,
            engine,
            selectedEntities,
            ct
         );

         CliUI.WriteSuccess("Reconciliation process finished!");
      }

      private async Task RunReconciliationAsync(
         IDataverseProvider source,
         IDataverseProvider target,
         ISyncEngine engine,
         List<string> entities,
         CancellationToken ct
      )
      {
         var options = new SyncOptions
         {
            StripMissingDependencies = true,
            MaxDegreeOfParallelism = 1 // Force single-threaded
         };

         foreach (var logicalName in entities)
         {
            AnsiConsole.MarkupLine(
               $"{SystemConstants.UiMarkup.BoldYellow}Reconciling " +
               $"{logicalName}...[/]"
            );

            int failedCount = 0;

            try
            {
               await AnsiConsole.Status()
                  .StartAsync("Initializing reconciliation...", async ctx =>
                  {
                     var recordProgress = new Progress<(int Processed, int Total, bool Success)>(p =>
                     {
                        if (!p.Success)
                           failedCount++;

                        var desc = $"[yellow]Reconciling {logicalName}...[/] " +
                           $"{p.Processed}/{p.Total} records processed";

                        if (failedCount > 0)
                           desc +=
                              $" {SystemConstants.UiMarkup.Red}({failedCount} failed)[/]";

                        ctx.Status(desc);
                     });

                     await _reconciliationService.PerformReconciliationAsync(
                        logicalName,
                        source,
                        target,
                        engine,
                        options,
                        recordProgress,
                        ct
                     );
                  });
            }
            catch (OperationCanceledException)
            {
               throw;
            }
            catch (Exception ex)
            {
               var baseEx = ex.GetBaseException();
               CliUI.WriteError(
                  $"Reconciliation aborted due to a critical error: " +
                  $"{baseEx.Message}"
               );

               break;
            }
         }
      }
   }
}
