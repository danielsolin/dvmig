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
         IEnvironmentValidator validator,
         ISchemaService schemaService,
         ISyncStateTracker stateTracker,
         ILogger logger
      ) : base(connectionManager, pluginService, sourceDateService, validator, schemaService, stateTracker, logger)
      {
         _reconciliationService = reconciliationService;
         _metadataService = metadataService;
      }

      public async Task HandleViewFailuresAsync()
      {
         var target = await ConnectionManager.ConnectAsync(
            ConnectionDirection.Target
         );

         if (target == null)
            return;

         bool isInitialized = await _reconciliationService.IsInitializedAsync(
            target,
            default
         );

         if (!isInitialized)
         {
            CliUI.WriteWarning(
               "Migration failure logging is not initialized on " +
               "this target."
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
               default
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
                  default
               )
            );

            CliUI.WriteSuccess("Failure log cleared.");
         }
      }

      public async Task HandleRecommendedReconciliationAsync()
      {
         var (source, target, engine) = await SetupSyncEngineAsync();

         if (source == null || target == null || engine == null)
            return;

         var recommendedEntities = SystemConstants.SyncSettings
            .RecommendedEntities;

         AnsiConsole.MarkupLine($"{SystemConstants.UiMarkup.BoldCyan}Recommended Reconciliation Order:[/]");

         foreach (var entity in recommendedEntities)
            AnsiConsole.MarkupLine($" - {entity}");

         if (!AnsiConsole.Confirm("Proceed with this reconciliation plan?", true))
         {
            CliUI.WriteWarning("Recommended reconciliation cancelled.");

            return;
         }

         var threads = AnsiConsole.Prompt(
            new SelectionPrompt<int>()
               .Title($"Select {SystemConstants.UiMarkup.Green}Max Parallelism[/] (Threads):")
               .AddChoices(new[] { 20, 10, 30, 40, 50, 5, 1 })
         );

         await RunReconciliationAsync(
            source,
            target,
            engine,
            recommendedEntities.ToList(),
            threads
         );

         CliUI.WriteSuccess("Recommended Reconciliation Finished!");
      }

      public async Task HandlePerformReconciliationAsync()
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

         var threads = AnsiConsole.Prompt(
            new SelectionPrompt<int>()
               .Title($"Select {SystemConstants.UiMarkup.Green}Max Parallelism[/] (Threads):")
               .AddChoices(
                  new[]
                  {
                     20,
                     10,
                     30,
                     40,
                     50,
                     5,
                     1
                  }
               )
         );

         await RunReconciliationAsync(
            source,
            target,
            engine,
            selectedEntities,
            threads
         );

         CliUI.WriteSuccess("Reconciliation process finished!");
      }

      private async Task RunReconciliationAsync(
         IDataverseProvider source,
         IDataverseProvider target,
         ISyncEngine engine,
         List<string> entities,
         int threads
      )
      {
         var options = new SyncOptions
         {
            StripMissingDependencies = true,
            MaxDegreeOfParallelism = threads
         };

         foreach (var logicalName in entities)
         {
            AnsiConsole.MarkupLine(
               $"{SystemConstants.UiMarkup.BoldYellow}Reconciling {logicalName}...[/]"
            );

            var sourceCount = await source.GetRecordCountAsync(logicalName);
            int processed = 0;
            int failedCount = 0;

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
                        $"({processed}/{sourceCount}) " +
                        $"[[{threads} threads]]";

                     var task = ctx.AddTask(taskName, true, sourceCount);
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
                            processed < sourceCount)
                           return;

                        lastUpdate = now;
                        task.Value = processed;

                        var recsPerSec = processed / sw.Elapsed.TotalSeconds;

                        var desc = $"{displayName} " +
                           $"({processed}/{sourceCount}) " +
                           $"[[{SystemConstants.UiMarkup.Green}{threads}t - {recsPerSec:F1} r/s[/]]] ";

                        if (failedCount > 0)
                           desc += $" {SystemConstants.UiMarkup.Red}({failedCount} failed)[/]";

                        task.Description = desc;
                     });

                     Logger.AttachProgress(new Progress<string>(
                        msg =>
                        {
                           var waitMark = SystemConstants.UiMarkup.Wait;
                           var throttleKey = SystemConstants
                              .ErrorKeywords.TooManyRequests;

                           var ordinal = StringComparison.Ordinal;
                           var ignoreCase =
                              StringComparison.OrdinalIgnoreCase;

                           bool isWait = msg.Contains(waitMark, ordinal);
                           bool isThrottled = msg.Contains(
                              throttleKey,
                              ignoreCase
                           );
                           bool isYellow = msg.StartsWith(
                              SystemConstants.UiMarkup.Yellow
                           );
                           bool isRed = msg.StartsWith(
                              SystemConstants.UiMarkup.Red
                           );

                           bool isCritical = isWait || isThrottled ||
                                             isYellow || isRed;

                           if (isCritical)
                              AnsiConsole.MarkupLine(msg);
                           else
                              AnsiConsole.MarkupLine(
                                 $"{SystemConstants.UiMarkup.Grey}{msg}[/]"
                              );
                        }
                     ));

                     try
                     {
                        await _reconciliationService.PerformReconciliationAsync(
                           logicalName,
                           source,
                           target,
                           engine,
                           options,
                           recordProgress,
                           default
                        );
                     }
                     finally
                     {
                        Logger.DetachProgress();
                     }

                     // Ensure it hits 100% if finished but logic doesn't align
                     task.Value = sourceCount;
                  });
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
