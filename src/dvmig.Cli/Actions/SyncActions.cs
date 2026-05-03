using dvmig.Core.Interfaces;
using dvmig.Core.Shared;
using dvmig.Core.Synchronization;
using Spectre.Console;

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

      public async Task HandleMigrationAsync(CancellationToken ct)
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
               .Title(
                  $"Select {SystemConstants.UiMarkup.Green}Max Parallelism[/]"
                  + " (Threads):"
               )
               .AddChoices(SystemConstants.SyncSettings.ParallelismOptions)
         );

         await RunMigrationAsync(
            engine,
            source,
            target,
            selectedEntities,
            threads,
            ct
         );

         CliUI.WriteSuccess("Migration Finished!");
      }

      public async Task HandleRecommendedSyncAsync(CancellationToken ct)
      {
         var (source, target, engine) = await SetupSyncEngineAsync();

         if (source == null || target == null || engine == null)
            return;

         var recommendedEntities = SystemConstants.SyncSettings
            .RecommendedEntities;

         AnsiConsole.MarkupLine(
            $"{SystemConstants.UiMarkup.BoldCyan}Recommended Sync Order:[/]"
         );

         foreach (var entity in recommendedEntities)
            AnsiConsole.MarkupLine($" - {entity}");

         if (!AnsiConsole.Confirm("Proceed with this sync plan?", true))
         {
            CliUI.WriteWarning("Recommended sync cancelled.");

            return;
         }

         var threads = AnsiConsole.Prompt(
            new SelectionPrompt<int>()
               .Title(
                  $"Select {SystemConstants.UiMarkup.Green}Max Parallelism[/] "
                  + " (Threads):"
               )
               .AddChoices(SystemConstants.SyncSettings.ParallelismOptions)
         );

         await RunMigrationAsync(
            engine,
            source,
            target,
            recommendedEntities.ToList(),
            threads,
            ct
         );

         CliUI.WriteSuccess("Recommended Migration Finished!");
      }

      private async Task RunMigrationAsync(
         ISyncEngine engine,
         IDataverseProvider source,
         IDataverseProvider target,
         List<string> entities,
         int maxThreads,
         CancellationToken ct
      )
      {
         foreach (var logicalName in entities)
         {
            AnsiConsole.MarkupLine(
               $"{SystemConstants.UiMarkup.BoldYellow}Migrating " +
               $"{logicalName}...[/]"
            );

            await engine.InitializeEntitySyncAsync(logicalName, ct);

            int processed = 0;
            int failedCount = 0;

            long totalCount = await _metadataService.GetRecordCountAsync(
               source,
               logicalName,
               ct
            );

            if (totalCount == 0)
            {
               AnsiConsole.MarkupLine(
                  $"{SystemConstants.UiMarkup.Grey}" +
                  $"No records found for {logicalName}.[/]"
               );

               continue;
            }

            // In the new ID-based approach, we start 'processed' at the count 
            // of records already existing on the target environment.
            var targetCount = await _metadataService.GetRecordCountAsync(
               target,
               logicalName,
               ct
            );

            processed = (int)Math.Min(totalCount, targetCount);

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
                  .StartAsync(
                     async ctx =>
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

                              var desc = $"{displayName} " +
                                 $"({processed}/{totalCount}) " +
                                 $"[[{SystemConstants.UiMarkup.Green}{maxThreads}t"
                                 + $" - {recsPerSec:F1} r/s[/]]] ";

                              if (failedCount > 0)
                                 desc +=
                                    $" {SystemConstants.UiMarkup.Red}" +
                                       $"({failedCount} failed)[/]";

                              task.Description = desc;
                           }
                        );

                        var options = new SyncOptions
                        {
                           StripMissingDependencies = true,
                           MaxDegreeOfParallelism = maxThreads
                        };

                        Logger.AttachProgress(
                           new Progress<string>(
                              msg =>
                              {
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
                              }
                           )
                        );

                        try
                        {
                           await engine.SyncEntityAsync(
                              logicalName,
                              options,
                              null,
                              recordProgress,
                              ct
                           );
                        }
                        finally
                        {
                           Logger.DetachProgress();
                        }

                        task.Value = totalCount;
                     }
                  );
            }
            catch (OperationCanceledException)
            {
               throw;
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
