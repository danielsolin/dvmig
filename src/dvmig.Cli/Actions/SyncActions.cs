using dvmig.Core.Interfaces;
using dvmig.Core.Synchronization;
using Spectre.Console;
using static dvmig.Core.Shared.SystemConstants;

namespace dvmig.Cli.Actions
{
   public class SyncActions : BaseActions
   {
      private readonly IMetadataService _metadataService;

      public SyncActions(
         ConnectionManager connectionManager,
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
         _metadataService = metadataService;
      }

      public async Task HandleMigrationAsync(
         CancellationToken ct,
         bool forceResync = false
      )
      {
         var (source, target, engine, _) = await SetupSyncEngineAsync();

         if (source == null || target == null || engine == null)
            return;

         var selectedEntities = await CliUI.SelectEntitiesAsync(
            _metadataService,
            source
         );

         if (selectedEntities == null || selectedEntities.Count == 0)
         {
            CliUI.WriteWarning("No entities selected.");

            return;
         }

         await HandleSyncFlowAsync(
            engine,
            source,
            target,
            selectedEntities,
            forceResync,
            ct
         );
      }

      public async Task HandleRecommendedSyncAsync(
         CancellationToken ct,
         bool forceResync = false
      )
      {
         var (source, target, engine, _) = await SetupSyncEngineAsync();

         if (source == null || target == null || engine == null)
            return;

         var recommendedEntities = SyncSettings.RecommendedEntities.ToList();

         var title = forceResync ? "Re-sync" : "Sync";
         AnsiConsole.MarkupLine(
            $"{UiMarkup.BoldCyan}Recommended {title} Order:[/]"
         );

         foreach (var entity in recommendedEntities)
            AnsiConsole.MarkupLine($" - {entity}");

         if (!AnsiConsole.Confirm($"Proceed with this {title} plan?", true))
         {
            CliUI.WriteWarning($"Recommended {title} cancelled.");

            return;
         }

         await HandleSyncFlowAsync(
            engine,
            source,
            target,
            recommendedEntities,
            forceResync,
            ct
         );
      }

      private async Task HandleSyncFlowAsync(
         ISyncEngine engine,
         IDataverseProvider source,
         IDataverseProvider target,
         List<string> entities,
         bool forceResync,
         CancellationToken ct
      )
      {
         var threads = AnsiConsole.Prompt(
            new SelectionPrompt<int>()
               .Title(
                  $"Select {UiMarkup.Green}Max Parallelism[/]"
                  + " (Threads):"
               )
               .AddChoices(SyncSettings.ParallelismOptions)
         );

         await RunMigrationAsync(
            engine,
            source,
            target,
            entities,
            threads,
            forceResync,
            ct
         );

         var actionName = forceResync ? "Re-sync" : "Migration";
         CliUI.WriteSuccess($"{actionName} Finished!");
      }

      private async Task RunMigrationAsync(
         ISyncEngine engine,
         IDataverseProvider source,
         IDataverseProvider target,
         List<string> entities,
         int maxThreads,
         bool forceResync,
         CancellationToken ct
      )
      {
         await AnsiConsole.Progress()
            .Columns(
               new ProgressColumn[]
               {
                  new TaskDescriptionColumn(),
                  new ProgressBarColumn(),
                  new PercentageColumn(),
                  new RemainingTimeColumn(),
                  new SpinnerColumn(),
               }
            )
            .StartAsync(
               async ctx =>
               {
                  foreach (var logicalName in entities)
                  {
                     var actionTitle = forceResync ? "Re-syncing" : "Migrating";
                     var displayName = char.ToUpper(logicalName[0]) +
                        logicalName.Substring(1);

                     int processed = 0;
                     int failedCount = 0;

                     long totalCount = await _metadataService
                        .GetRecordCountAsync(source, logicalName, ct);

                     if (totalCount == 0)
                     {
                        AnsiConsole.MarkupLine(
                           $"{UiMarkup.Grey}No records found for " +
                           $"{logicalName}.[/]"
                        );

                        continue;
                     }

                     if (!forceResync)
                     {
                        var targetCount = await _metadataService
                           .GetRecordCountAsync(target, logicalName, ct);

                        processed = (int)Math.Min(totalCount, targetCount);
                     }

                     string GetDesc(int p, long t, double r, int f)
                     {
                        var titleMarkup = $"{UiMarkup.BoldRed}{actionTitle} " +
                           $"{displayName}[/]";

                        var rateInfo = r > 0 ? $" - {r:F1} r/s" : "";
                        var desc = $"{titleMarkup} ({p}/{t}) " +
                           $"[[{UiMarkup.Green}{maxThreads}t{rateInfo}[/]]] ";

                        if (f > 0)
                        {
                           desc += $"{UiMarkup.Red}" +
                              $"({f} failed)[/]";
                        }

                        return desc;
                     }

                     var task = ctx.AddTask(
                        GetDesc(processed, totalCount, 0, 0),
                        true,
                        totalCount
                     );

                     task.Value = processed;

                     var sw = System.Diagnostics.Stopwatch.StartNew();
                     var lastUpdate = DateTime.MinValue;
                     var recordProgress = new Progress<bool>(
                        success =>
                        {
                           processed++;

                           if (!success)
                              failedCount++;

                           var now = DateTime.Now;
                           if (now - lastUpdate < TimeSpan.FromSeconds(1) &&
                               processed < totalCount)
                              return;

                           lastUpdate = now;
                           task.Value = processed;

                           var swElapsed = sw.Elapsed.TotalSeconds;
                           var recsPerSec = processed / swElapsed;

                           task.Description = GetDesc(
                              processed,
                              totalCount,
                              recsPerSec,
                              failedCount
                           );
                        }
                     );

                     var options = new SyncOptions
                     {
                        StripMissingDependencies = true,
                        MaxDegreeOfParallelism = maxThreads,
                        ForceResync = forceResync
                     };

                     Logger.AttachProgress(
                        new Progress<string>(
                           msg =>
                           {
                              bool isCritical =
                                 msg.Contains(
                                    UiMarkup.Wait,
                                    StringComparison.Ordinal
                                 ) ||
                                 msg.Contains(
                                    ErrorKeywords.TooManyRequests,
                                    StringComparison.OrdinalIgnoreCase
                                 ) ||
                                 msg.StartsWith(UiMarkup.Yellow) ||
                                 msg.StartsWith(UiMarkup.Red);

                              if (isCritical)
                                 AnsiConsole.MarkupLine(msg);
                           }
                        )
                     );

                     try
                     {
                        await engine.SyncAsync(
                           logicalName,
                           options,
                           null,
                           recordProgress,
                           ct
                        );
                     }
                     catch (Exception ex)
                     {
                        var baseEx = ex.GetBaseException();
                        CliUI.WriteError(
                           $"Sync aborted for {logicalName}: " +
                           $"{baseEx.Message}"
                        );
                     }
                     finally
                     {
                        Logger.DetachProgress();
                        task.Value = totalCount;
                        task.StopTask();
                     }
                  }
               }
            );
      }
   }
}
