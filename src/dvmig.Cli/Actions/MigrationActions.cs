using dvmig.Core.Interfaces;
using dvmig.Core.Shared;
using dvmig.Core.Synchronization;
using Serilog;
using Spectre.Console;

namespace dvmig.Cli.Actions
{
   public class MigrationActions : BaseActions
   {
      private readonly IMetadataService _metadataService;

      public MigrationActions(
         ConnectionManager connectionManager,
         IMetadataService metadataService,
         ISetupService setupService,
         ISourceDateService sourceDateService,
         IEnvironmentValidator validator,
         ISchemaManager schemaManager,
         ISyncStateTracker stateTracker,
         ILogger logger
      ) : base(connectionManager, setupService, sourceDateService, validator, schemaManager, stateTracker, logger)
      {
         _metadataService = metadataService;
      }

      public async Task HandleMigrationAsync()
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
            CliUI.WriteWarning("No entities selected.");

            return;
         }

         var threads = AnsiConsole.Prompt(
            new SelectionPrompt<int>()
               .Title($"Select {SystemConstants.UiMarkup.Green}Max Parallelism[/] (Threads):")
               .AddChoices(new[] { 10, 20, 5, 1 })
         );

         await RunMigrationAsync(engine, source, selectedEntities, threads);
         CliUI.WriteSuccess("Migration Finished!");
      }

      public async Task HandleRecommendedSyncAsync()
      {
         var (source, target, engine) = await SetupSyncEngineAsync();

         if (source == null || target == null || engine == null)
            return;

         var recommendedEntities = SystemConstants.SyncSettings
            .RecommendedEntities;

         AnsiConsole.MarkupLine($"{SystemConstants.UiMarkup.BoldCyan}Recommended Sync Order:[/]");

         foreach (var entity in recommendedEntities)
            AnsiConsole.MarkupLine($" - {entity}");

         if (!AnsiConsole.Confirm("Proceed with this sync plan?", true))
         {
            CliUI.WriteWarning("Recommended sync cancelled.");

            return;
         }

         var threads = AnsiConsole.Prompt(
            new SelectionPrompt<int>()
               .Title($"Select {SystemConstants.UiMarkup.Green}Max Parallelism[/] (Threads):")
               .AddChoices(new[] { 10, 20, 5, 1 })
         );

         await RunMigrationAsync(
            engine,
            source,
            recommendedEntities.ToList(),
            threads
         );

         CliUI.WriteSuccess("Recommended Migration Finished!");
      }

      private async Task RunMigrationAsync(
         ISyncEngine engine,
         IDataverseProvider source,
         List<string> entities,
         int maxThreads
      )
      {
         foreach (var logicalName in entities)
         {
            AnsiConsole.MarkupLine(
               $"{SystemConstants.UiMarkup.BoldYellow}Migrating {logicalName}...[/]"
            );

            await engine.InitializeEntitySyncAsync(logicalName);

            int processed = 0;
            int failedCount = 0;

            if (StateTracker.StateExists())
            {
               var syncedIds = await StateTracker.GetSyncedIdsAsync();

               if (syncedIds.Count > 0)
               {
                  var resumeMsg = $"Previous migration state found " +
                     $"for {logicalName} " +
                     $"({syncedIds.Count} " +
                     "records already synced). " +
                     "Resume (y) or start over (n) ?";

                  if (!AnsiConsole.Confirm(resumeMsg, true))
                  {
                     await StateTracker.ClearStateAsync();
                     await engine.InitializeEntitySyncAsync(logicalName);
                  }
                  else
                     processed = syncedIds.Count;
               }
            }

            long totalCount = await _metadataService.GetRecordCountAsync(
               source,
               logicalName,
               default
            );

            if (totalCount == 0)
            {
               AnsiConsole.MarkupLine(
                  $"{SystemConstants.UiMarkup.Grey}" +
                  $"No records found for {logicalName}.[/]"
               );

               continue;
            }

            try
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
                  .StartAsync(async ctx =>
                  {
                     var displayName = char.ToUpper(logicalName[0]) +
                        logicalName.Substring(1);

                     var taskName = $"{displayName} " +
                        $"({processed}/{totalCount}) " +
                        $"[[{maxThreads} threads]]";

                     var task = ctx.AddTask(taskName, true, totalCount);
                     task.Value = processed;

                     var sw = System.Diagnostics.Stopwatch.StartNew();
                     var lastUpdate = DateTime.MinValue;
                     var recordProgress = new Progress<bool>(success =>
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

                        var desc = $"{displayName} " +
                           $"({processed}/{totalCount}) " +
                           $"[[{SystemConstants.UiMarkup.Green}{maxThreads}t - " +
                           $"{recsPerSec:F1} r/s[/]]] ";

                        if (failedCount > 0)
                           desc += $" {SystemConstants.UiMarkup.Red}({failedCount} failed)[/]";

                        task.Description = desc;
                     });

                     var options = new SyncOptions
                     {
                        StripMissingDependencies = true,
                        MaxDegreeOfParallelism = maxThreads
                     };

                     var progressReporter = new Progress<string>(msg =>
                     {
                        // Ensure we show wait/retry/throttle messages even
                        // during the progress bar display.
                        bool isCritical =
                           msg.Contains(
                              SystemConstants.UiMarkup.Wait,
                              StringComparison.Ordinal
                           ) ||
                           msg.Contains(
                              SystemConstants.ErrorKeywords.TooManyRequests,
                              StringComparison.OrdinalIgnoreCase
                           ) ||
                           msg.StartsWith(SystemConstants.UiMarkup.Yellow) ||
                           msg.StartsWith(SystemConstants.UiMarkup.Red);

                        if (isCritical)
                           AnsiConsole.MarkupLine(msg);
                     });

                     await engine.SyncEntityAsync(
                        logicalName,
                        options,
                        null,
                        progressReporter,
                        recordProgress,
                        default
                     );

                     // Ensure it hits 100% even if there were rounding or 
                     // async reporting skips.
                     task.Value = totalCount;
                  });
            }
            catch (Exception ex)
            {
               var baseEx = ex.GetBaseException();
               CliUI.WriteError(
                  $"Sync aborted due to a critical error: {baseEx.Message}"
               );

               break;
            }
         }
      }
   }
}
