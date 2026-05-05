using dvmig.Core.Interfaces;
using dvmig.Core.Shared;
using dvmig.Core.Synchronization;
using Spectre.Console;
using static dvmig.Core.Shared.SystemConstants;

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
      protected readonly ISourceDataService SourceDataService;
      protected readonly IValidationService Validator;
      protected readonly ISchemaService SchemaService;
      protected readonly ILogger Logger;

      protected BaseActions(
         ConnectionManager connectionManager,
         IPluginService pluginService,
         ISourceDataService sourceDataService,
         IValidationService validator,
         ISchemaService schemaService,
         ILogger logger
      )
      {
         ConnectionManager = connectionManager;
         PluginService = pluginService;
         SourceDataService = sourceDataService;
         Validator = validator;
         SchemaService = schemaService;
         Logger = logger;
      }

      /// <summary>
      /// Sets up the synchronization engine by connecting to both source
      /// and target, and ensuring the target environment is prepared.
      /// </summary>
      protected async Task<(
         IDataverseProvider? Source,
         IDataverseProvider? Target,
         ISyncEngine? Engine,
         IUserResolver? UserResolver
      )> SetupSyncEngineAsync()
      {
         var source = await ConnectionManager.ConnectAsync(
            ConnectionDirection.Source
         );

         if (source == null)
            return (null, null, null, null);

         var target = await ConnectionManager.ConnectAsync(
            ConnectionDirection.Target
         );

         if (target == null)
            return (null, null, null, null);

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
         var syncStateService = new SyncStateService();
         var resilience = new SyncResilienceService(
            source,
            target,
            syncStateService,
            Logger
         );

         var entityService = new EntityService(Logger);
         var metadataService = new MetadataService(Logger, target);
         var failureService = new FailureService(target, Logger);
         var relationshipService = new RelationshipService(target, Logger);

         var engine = new SyncEngine(
            source,
            target,
            userResolver,
            Logger,
            entityService,
            resilience,
            metadataService,
            failureService,
            SourceDataService,
            syncStateService,
            relationshipService
         );

         return (source, target, engine, userResolver);
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
         catch (OperationCanceledException)
         {
            throw;
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
