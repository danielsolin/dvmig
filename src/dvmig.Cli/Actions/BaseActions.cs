using dvmig.Core.Interfaces;
using dvmig.Core.Shared;
using dvmig.Core.Synchronization;
using dvmig.Core.Provisioning;
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
      protected readonly IPluginService PluginService;
      protected readonly ISourceDateService SourceDateService;
      protected readonly IValidationService Validator;
      protected readonly ISchemaService SchemaService;
      protected readonly ISyncStateService StateService;
      protected readonly ILogger Logger;

      protected BaseActions(
         ConnectionManager connectionManager,
         IPluginService pluginService,
         ISourceDateService sourceDateService,
         IValidationService validator,
         ISchemaService schemaService,
         ISyncStateService stateService,
         ILogger logger
      )
      {
         ConnectionManager = connectionManager;
         PluginService = pluginService;
         SourceDateService = sourceDateService;
         Validator = validator;
         SchemaService = schemaService;
         StateService = stateService;
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

         bool isReady = await Validator.ValidateTargetEnvironmentAsync(
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

         var userResolver = new UserResolver(source, target, Logger);
         var retryService = new RetryService(Logger);
         var entityService = new EntityService(Logger);
         var errorService = new ErrorService(
            target,
            SourceDateService,
            Logger
         );

         var dependencyResolver = new DependencyResolver(source, Logger);
         var statusService = new StatusService(
            target,
            SourceDateService,
            Logger
         );

         var metadataService = new MetadataService(Logger, target);
         var failureService = new FailureService(target, Logger);

         var engine = new SyncEngine(
            source,
            target,
            userResolver,
            StateService,
            Logger,
            retryService,
            entityService,
            errorService,
            dependencyResolver,
            statusService,
            metadataService,
            failureService,
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
               Logger, 
               async () =>
               {
                  await SchemaService.CreateSchemaAsync(target);
                  await PluginService.DeployPluginAsync(target, null);
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
