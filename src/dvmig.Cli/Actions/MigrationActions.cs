using dvmig.Cli.Infrastructure;
using dvmig.Core.Interfaces;
using dvmig.Core.Metadata;
using dvmig.Core.Synchronization;
using Serilog;
using Spectre.Console;

namespace dvmig.Cli.Actions
{
   public class MigrationActions
   {
      private readonly ConnectionManager _connectionManager;
      private readonly IMetadataService _metadataService;
      private readonly ISetupService _setupService;
      private readonly ISyncStateTracker _stateTracker;
      private readonly ILogger _logger;

      public MigrationActions(
         ConnectionManager connectionManager,
         IMetadataService metadataService,
         ISetupService setupService,
         ISyncStateTracker stateTracker,
         ILogger logger)
      {
         _connectionManager = connectionManager;
         _metadataService = metadataService;
         _setupService = setupService;
         _stateTracker = stateTracker;
         _logger = logger;
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
                 .Title("Select [green]Max Parallelism[/] (Threads):")
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

         var recommendedEntities = new List<string>
            {
               "account",
               "contact",
               "task",
               "phonecall",
               "email",
               "appointment"
            };

         AnsiConsole.MarkupLine(
             "[bold cyan]Recommended Sync Order:[/]"
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
                 .Title("Select [green]Max Parallelism[/] (Threads):")
                 .AddChoices(new[] { 10, 20, 5, 1 })
         );

         await RunMigrationAsync(
             engine,
             source,
             recommendedEntities,
             threads
         );
         CliUI.WriteSuccess("Recommended Migration Finished!");
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
            var prepareMsg = "[yellow]Target environment is not " +
                             "prepared for migration. " +
                             "Prepare it now?[/]";

            if (AnsiConsole.Confirm(prepareMsg, true))
               await HandleInstallAsync(target);
            else
            {
               CliUI.WriteError(
                   "Migration cannot proceed without components."
               );

               return (null, null, null);
            }
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

      private async Task HandleInstallAsync(IDataverseProvider target)
      {
         await CliUI.RunStatusAsync(
             "Installing components...",
             async progress =>
             {
                await _setupService.CreateSchemaAsync(target, progress);
                await _setupService.DeployPluginAsync(target, progress);
             }
         );

         CliUI.WriteSuccess("Installation Finished!");
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
                $"[bold yellow]Migrating {logicalName}...[/]"
            );

            await engine.InitializeEntitySyncAsync(logicalName);

            int processed = 0;
            if (_stateTracker.StateExists())
            {
               var syncedIds = await _stateTracker.GetSyncedIdsAsync();
               if (syncedIds.Count > 0)
               {
                  var resumeMsg = $"Previous migration state found " +
                                  $"for {logicalName} " +
                                  $"({syncedIds.Count} " +
                                  "records already synced). " +
                                  "Resume (y) or start over (n) ?";

                  if (!AnsiConsole.Confirm(resumeMsg, true))
                  {
                     await _stateTracker.ClearStateAsync();
                     await engine.InitializeEntitySyncAsync(
                         logicalName
                     );
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
                   $"[grey]No records found for {logicalName}.[/]"
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
                      var taskName = $"Syncing {logicalName} " +
                                         $"({processed}/{totalCount}) " +
                                         $"[[{maxThreads} threads]]";

                      var task = ctx.AddTask(taskName, true, totalCount);
                      task.Value = processed;

                      var recordProgress = new Progress<bool>(success =>
                          {
                             if (success)
                             {
                                processed++;
                                task.Value = processed;
                                task.Description =
                                        $"Syncing {logicalName} " +
                                        $"({processed}/{totalCount}) " +
                                        $"[[{maxThreads} threads]]";
                             }
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
                             if (msg.Contains(
                                    "WAIT",
                                    StringComparison.Ordinal
                                 ) || 
                                 msg.Contains(
                                    "throttle",
                                    StringComparison.OrdinalIgnoreCase
                                 ) ||
                                 msg.StartsWith("[yellow]") || 
                                 msg.StartsWith("[red]"))
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
