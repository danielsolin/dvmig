using dvmig.Cli.Infrastructure;
using dvmig.Core.Interfaces;
using dvmig.Core.Metadata;
using dvmig.Core.Shared;
using dvmig.Core.Synchronization;
using Serilog;
using Spectre.Console;

namespace dvmig.Cli.Actions
{
   public class ReconciliationActions
   {
      private readonly ConnectionManager _connectionManager;
      private readonly IReconciliationService _reconciliationService;
      private readonly IMetadataService _metadataService;
      private readonly ISetupService _setupService;
      private readonly ISyncStateTracker _stateTracker;
      private readonly ILogger _logger;

      public ReconciliationActions(
         ConnectionManager connectionManager,
         IReconciliationService reconciliationService,
         IMetadataService metadataService,
         ISetupService setupService,
         ISyncStateTracker stateTracker,
         ILogger logger
      )
      {
         _connectionManager = connectionManager;
         _reconciliationService = reconciliationService;
         _metadataService = metadataService;
         _setupService = setupService;
         _stateTracker = stateTracker;
         _logger = logger;
      }

      public async Task HandleViewFailuresAsync()
      {
         var target = await _connectionManager.ConnectAsync(
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
               async _ => await _reconciliationService.ClearFailuresAsync(
                  target,
                  default
               )
            );

            CliUI.WriteSuccess("Failure log cleared.");
         }
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
               .Title("Select [green]Max Parallelism[/] (Threads):")
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

         var options = new SyncOptions
         {
            StripMissingDependencies = true,
            MaxDegreeOfParallelism = threads
         };

         foreach (var logicalName in selectedEntities)
         {
            AnsiConsole.MarkupLine(
               $"[bold yellow]Reconciling {logicalName}...[/]"
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
                           $"[[[green]{threads}t - {recsPerSec:F1} r/s[/]]] ";

                        if (failedCount > 0)
                           desc += $" [red]({failedCount} failed)[/]";

                        task.Description = desc;
                     });

                     await _reconciliationService.PerformReconciliationAsync(
                        logicalName,
                        source,
                        target,
                        engine,
                        options,
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
                              else
                                 AnsiConsole.MarkupLine(
                                    $"{SystemConstants.UiMarkup.Grey}{msg}[/]"
                                 );
                           }
                        ),
                        recordProgress,
                        default
                     );

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

         CliUI.WriteSuccess("Reconciliation process finished!");
      }

      private async Task<(
         IDataverseProvider? Source,
         IDataverseProvider? Target,
         ISyncEngine? Engine
      )> SetupSyncEngineAsync()
      {
         var source = await _connectionManager.ConnectAsync(
            ConnectionDirection.Source
         );

         if (source == null)
            return (null, null, null);

         var target = await _connectionManager.ConnectAsync(
            ConnectionDirection.Target
         );

         if (target == null)
            return (null, null, null);

         bool isReady = await _setupService.IsEnvironmentReadyAsync(
            target,
            default
         );

         if (!isReady)
         {
            CliUI.WriteError(
               "Target environment is not prepared. " +
               "Please run 'Install dvmig Components' first."
            );

            return (null, null, null);
         }

         var userMapper = new UserMapper(source, target, _logger);
         var retryStrategy = new RetryStrategy(_logger);
         var entityPreparer = new EntityPreparer(_logger);
         var errorHandler = new SyncErrorHandler(
            target,
            _setupService,
            _logger
         );

         var dependencyResolver = new DependencyResolver(source, _logger);
         var statusTransitionHandler = new StatusTransitionHandler(
            target,
            _setupService,
            _logger
         );

         var metadataCache = new MetadataCache(target, _logger);
         var failureLogger = new FailureLogger(target, _logger);

         var engine = new SyncEngine(
            source,
            target,
            userMapper,
            _setupService,
            _stateTracker,
            _logger,
            retryStrategy,
            entityPreparer,
            errorHandler,
            dependencyResolver,
            statusTransitionHandler,
            metadataCache,
            failureLogger
         );

         return (source, target, engine);
      }
   }
}
