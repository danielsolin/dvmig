using dvmig.Core.Interfaces;
using dvmig.Core.Shared;
using dvmig.Core.Synchronization;
using Serilog;
using Spectre.Console;

namespace dvmig.Cli.Actions
{
   /// <summary>
   /// Base class for CLI action handlers, providing shared logic for 
   /// sync engine initialization and component installation.
   /// </summary>
   public abstract class BaseActions
   {
      protected readonly ConnectionManager ConnectionManager;
      protected readonly ISetupService SetupService;
      protected readonly ISourceDateService SourceDateService;
      protected readonly IEnvironmentValidator Validator;
      protected readonly ISchemaManager SchemaManager;
      protected readonly ISyncStateTracker StateTracker;
      protected readonly ILogger Logger;

      protected BaseActions(
         ConnectionManager connectionManager,
         ISetupService setupService,
         ISourceDateService sourceDateService,
         IEnvironmentValidator validator,
         ISchemaManager schemaManager,
         ISyncStateTracker stateTracker,
         ILogger logger
      )
      {
         ConnectionManager = connectionManager;
         SetupService = setupService;
         SourceDateService = sourceDateService;
         Validator = validator;
         SchemaManager = schemaManager;
         StateTracker = stateTracker;
         Logger = logger;
      }

      /// <summary>
      /// Sets up the synchronization engine by connecting to both source 
      /// and target, and ensuring the target environment is prepared.
      /// </summary>
      protected async Task<(
         IDataverseProvider? Source,
         IDataverseProvider? Target,
         ISyncEngine? Engine
      )> SetupSyncEngineAsync()
      {
         var source = await ConnectionManager.ConnectAsync(
            ConnectionDirection.Source
         );

         if (source == null)
            return (null, null, null);

         var target = await ConnectionManager.ConnectAsync(
            ConnectionDirection.Target
         );

         if (target == null)
            return (null, null, null);

         bool isReady = await Validator.IsEnvironmentReadyAsync(
            target,
            default
         );

         if (!isReady)
         {
            AnsiConsole.MarkupLine(
               $"{SystemConstants.UiMarkup.Yellow}Target environment is " +
               "not prepared. Installing required dvmig components...[/]"
            );

            await HandleInstallAsync(target);
         }

         var userMapper = new UserMapper(source, target, Logger);
         var retryStrategy = new RetryStrategy(Logger);
         var entityPreparer = new EntityPreparer(Logger);
         var errorHandler = new SyncErrorHandler(
            target,
            SourceDateService,
            Logger
         );

         var dependencyResolver = new DependencyResolver(source, Logger);
         var statusTransitionHandler = new StatusTransitionHandler(
            target,
            SourceDateService,
            Logger
         );

         var metadataCache = new MetadataCache(target, Logger);
         var failureLogger = new FailureLogger(target, Logger);

         var engine = new SyncEngine(
            source,
            target,
            userMapper,
            SetupService,
            StateTracker,
            Logger,
            retryStrategy,
            entityPreparer,
            errorHandler,
            dependencyResolver,
            statusTransitionHandler,
            metadataCache,
            failureLogger,
            SourceDateService
         );

         return (source, target, engine);
      }

      /// <summary>
      /// Handles the installation of dvmig components on the 
      /// target environment.
      /// </summary>
      protected async Task HandleInstallAsync(IDataverseProvider target)
      {
         try
         {
            await CliUI.RunStatusAsync(
               "Installing components...",
               async progress =>
               {
                  await SchemaManager.CreateSchemaAsync(target, progress);
                  await SetupService.DeployPluginAsync(target, progress);
               }
            );

            CliUI.WriteSuccess("Installation Finished!");
         }
         catch (Exception ex)
         {
            var baseEx = ex.GetBaseException();
            CliUI.WriteError(
               $"Installation failed: {baseEx.Message}"
            );
         }
      }
   }
}
