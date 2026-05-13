using dvmig.Core.Interfaces;
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
      protected readonly IEnvironmentService EnvironmentService;
      protected readonly ILogger Logger;
      protected readonly IEntityService EntityService;
      protected readonly ISettingsService SettingsService;

      protected BaseActions(
         ConnectionManager connectionManager,
         IEnvironmentService environmentService,
         ILogger logger,
         IEntityService entityService,
         ISettingsService settingsService
      )
      {
         ConnectionManager = connectionManager;
         EnvironmentService = environmentService;
         Logger = logger;
         EntityService = entityService;
         SettingsService = settingsService;
      }

      /// <summary>
      /// Sets up the synchronization engine by connecting to both source
      /// and target, and ensuring the target environment is prepared.
      /// </summary>
      protected async Task<(
         IDataverseProvider? Source,
         IDataverseProvider? Target,
         ISyncEngine? Engine,
         IUserService? UserResolver
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

         bool isReady = await EnvironmentService.ValidateTargetEnvironmentAsync(
            target,
            default
         );

         if (!isReady)
         {
            AnsiConsole.MarkupLine(
               $"{UiMarkup.Yellow}Target environment is " +
               "not prepared. Installing required dvmig components...[/]"
            );

            await HandleInstallAsync(target);
         }

         // Clear connection noise and show clean summary
         CliUI.WriteHeader();

         var sourceUrl = GetProviderUrl(source);
         var targetUrl = GetProviderUrl(target);

         AnsiConsole.Write(
            new Panel(
               new Grid()
                  .AddColumn(new GridColumn().NoWrap())
                  .AddColumn(new GridColumn().PadLeft(2))
                  .AddRow(
                     $"{UiMarkup.BoldGreen}Source:[/]", 
                     $"{UiMarkup.BoldYellow}{sourceUrl}[/]"
                  )
                  .AddRow(
                     $"{UiMarkup.BoldGreen}Target:[/]", 
                     $"{UiMarkup.BoldYellow}{targetUrl}[/]"
                  )
            )
            .Header($"{UiMarkup.BoldCyan} Environments [/]")
            .Expand()
         );

         AnsiConsole.WriteLine();

         if (ConnectionManager.UserResolver == null)
         {
            ConnectionManager.UserResolver = new UserService(
               Logger, 
               source, 
               target
            );
         }

         var userResolver = ConnectionManager.UserResolver;
         var syncStateService = new SyncStateService();

         // We create a fresh EntityService for this sync run that's bound 
         // to the current target for metadata caching.
         var syncEntityService = new EntityService(Logger, target);

         var engine = new SyncEngine(
            source,
            target,
            userResolver,
            Logger,
            syncEntityService,
            syncStateService
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
               async () => await EnvironmentService.InstallComponentsAsync(
                  target
               ),
               lineByLine: true
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

      private static string GetProviderUrl(IDataverseProvider provider)
      {
         var conn = provider.ConnectionString;

         return conn.Contains("://")
            ? conn.Split("://")[1].Split(";")[0].Split("/")[0]
            : "Connected";
      }
   }
}
