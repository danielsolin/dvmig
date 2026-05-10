using Bogus;
using CT = System.Threading.CancellationToken;
using dvmig.Cli.Actions;
using dvmig.Cli.Providers;
using dvmig.Core.Interfaces;
using dvmig.Core.Providers;
using dvmig.Core.Provisioning;
using dvmig.Core.Settings;
using dvmig.Core.Shared;
using dvmig.Core.Synchronization;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Tooling.Connector;
using Polly;
using Polly.Retry;
using Serilog;
using SerilogLogger = Serilog.ILogger;
using Spectre.Console;
using static dvmig.Core.Shared.SystemConstants;
using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.ServiceModel;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using var clonedClient = _client.Clone();
using var reader = new StreamReader(stream);
using var stream = assembly.GetManifestResourceStream(resourceName);

// --- Source: dvmig.Cli\Actions\BaseActions.cs ---

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
      protected readonly IValidationService Validator;
      protected readonly ISchemaService SchemaService;
      protected readonly ILogger Logger;
      protected readonly IEntityService EntityService;
      protected readonly ISettingsService SettingsService;

      protected BaseActions(
         ConnectionManager connectionManager,
         IPluginService pluginService,
         IValidationService validator,
         ISchemaService schemaService,
         ILogger logger,
         IEntityService entityService,
         ISettingsService settingsService
      )
      {
         ConnectionManager = connectionManager;
         PluginService = pluginService;
         Validator = validator;
         SchemaService = schemaService;
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
            ConnectionManager.UserResolver = new UserService(Logger, source, target);
            ConnectionManager.UserMappingsCached = false;
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

      private string GetProviderUrl(IDataverseProvider provider)
      {
         var conn = provider.ConnectionString;

         return conn.Contains("://")
            ? conn.Split("://")[1].Split(";")[0].Split("/")[0]
            : "Connected";
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


// --- Source: dvmig.Cli\Actions\MaintenanceActions.cs ---

namespace dvmig.Cli.Actions
{
   public class MaintenanceActions : BaseActions
   {
      private readonly ISeedingService _seedingService;
      private readonly IWipeDataService _wipeDataService;

      public MaintenanceActions(
         ConnectionManager connectionManager,
         ISeedingService seedingService,
         IWipeDataService wipeDataService,
         IPluginService pluginService,
         IValidationService validator,
         ISchemaService schemaService,
         IEntityService entityService,
         ILogger logger,
         ISettingsService settingsService
      ) : base(
         connectionManager,
         pluginService,
         validator,
         schemaService,
         logger,
         entityService,
         settingsService
      )
      {
         _seedingService = seedingService;
         _wipeDataService = wipeDataService;
      }

      public async Task HandleViewFailuresAsync(CancellationToken ct)
      {
         var target = await ConnectionManager.ConnectAsync(
            ConnectionDirection.Target
         );

         if (target == null)
            return;

         var engine = new SyncEngine(
            null!,
            target,
            null!,
            Logger,
            EntityService,
            new SyncStateService()
         );

         bool isInitialized = await engine.IsFailureLoggingInitializedAsync(
            target,
            ct
         );

         if (!isInitialized)
         {
            CliUI.WriteWarning(
               "Migration failure logging is not initialized on this target."
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
            async () => await engine.GetFailuresAsync(
               target,
               null,
               ct
            )
         );

         if (failures.Count == 0)
         {
            CliUI.WriteSuccess(
               "No migration failures recorded in Target environment."
            );

            return;
         }

         var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Red)
            .Title("[bold red]Migration Failures[/]");

         table.AddColumn("[bold]Entity[/]");
         table.AddColumn("[bold]Source ID[/]");
         table.AddColumn("[bold]Timestamp (UTC)[/]");
         table.AddColumn("[bold]Error Message[/]");

         foreach (var failure in failures)
         {
            table.AddRow(
               failure.EntityLogicalName,
               failure.SourceId,
               failure.TimestampUtc.ToString("yyyy-MM-dd HH:mm:ss"),
               failure.ErrorMessage
            );
         }

         AnsiConsole.Write(table);

         var clearLog = "Would you like to clear the failure log on " +
                        "the target?";

         if (AnsiConsole.Confirm(clearLog, false))
         {
            await CliUI.RunStatusAsync(
               "Clearing failure log...",
               Logger,
               async () => await engine.ClearFailuresAsync(
                  target,
                  ct
               )
            );

            CliUI.WriteSuccess("Failure log cleared.");
         }
      }

      public async Task HandleSeedingAsync(CancellationToken ct)
      {
         var provider = await ConnectionManager.ConnectAsync(
            ConnectionDirection.Source
         );

         if (provider == null)
            return;

         var prompt =
            $"How many {SystemConstants.UiMarkup.BoldBlue}Accounts[/] " +
            "(with related Contacts and Activities) would you like " +
            "to generate?";

         int count = AnsiConsole.Ask<int>(prompt, 100);

         await CliUI.RunStatusAsync(
            "Seeding data...",
            Logger,
            async () => await _seedingService.SeedSampleDataAsync(
               provider,
               count,
               ct
            )
         );

         CliUI.WriteSuccess("Seeding Finished!");
      }

      public async Task HandleInstallMenuAsync(CancellationToken ct)
      {
         var provider = await ConnectionManager.ConnectAsync(
            ConnectionDirection.Target
         );

         if (provider == null)
            return;

         await HandleInstallAsync(provider);
      }

      public async Task HandleTargetComponentsCleanupAsync(CancellationToken ct)
      {
         var provider = await ConnectionManager.ConnectAsync(
            ConnectionDirection.Target
         );

         if (provider == null)
            return;

         var promptMsg =
            $"{SystemConstants.UiMarkup.Red}Are you sure you want " +
            "to remove all dvmig system components (schema and plugins) " +
            "from this environment?[/]";

         if (!AnsiConsole.Confirm(promptMsg, false))
            return;

         try
         {
            await CliUI.RunStatusAsync(
               "Uninstalling components...",
               Logger,
               async () =>
               {
                  Logger.Information("Cleaning target environment...");

                  await PluginService.RemovePluginAsync(provider, ct);

                  await provider.ExecuteAsync(new PublishAllXmlRequest(), ct);

                  await SchemaService.DropSchemaAsync(provider, ct);

                  Logger.Information("Environment cleanup completed.");
               }
            );

            CliUI.WriteSuccess("Uninstallation Finished!");
         }
         catch (OperationCanceledException)
         {
            throw;
         }
         catch (Exception ex)
         {
            var baseEx = ex.GetBaseException();

            CliUI.WriteError(
               "Cleanup failed. " + baseEx.Message
            );
         }
      }

      public async Task HandleSourceDataCleanupAsync(CancellationToken ct)
      {
         await HandleDataCleanupInternalAsync(ConnectionDirection.Source, ct);
      }

      public async Task HandleTargetDataCleanupAsync(CancellationToken ct)
      {
         await HandleDataCleanupInternalAsync(ConnectionDirection.Target, ct);
      }

      private async Task HandleDataCleanupInternalAsync(
         ConnectionDirection direction,
         CancellationToken ct
      )
      {
         var provider = await ConnectionManager.ConnectAsync(direction);

         if (provider == null)
            return;

         var envName = direction == ConnectionDirection.Source
            ? "SOURCE"
            : "TARGET";

         var wipeChoice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
               .Title($"What data do you want to wipe on {envName}?")
               .AddChoices(
                  new[]
                  {
                     "All Recommended Entities",
                     "Select Specific Entities"
                  }
               )
         );

         List<string>? selectedEntities = null;

         if (wipeChoice == "Select Specific Entities")
         {
            selectedEntities = await CliUI.SelectEntitiesAsync(
               EntityService,
               provider
            );

            if (selectedEntities == null || selectedEntities.Count == 0)
            {
               CliUI.WriteWarning("No entities selected. Wipe cancelled.");

               return;
            }
         }
         else
         {
            var list = SyncSettings.RecommendedEntities
               .Select(e => char.ToUpper(e[0]) + e.Substring(1))
               .ToList();

            var entityNames = list.Count > 1
               ? string.Join(", ", list.Take(list.Count - 1)) +
                 " and " + list.Last()
               : list.FirstOrDefault() ?? string.Empty;

            var warningTable = new Table()
               .Border(TableBorder.None)
               .HideHeaders()
               .AddColumn("Icon")
               .AddColumn("Message");

            warningTable.AddRow(
               "[bold red]![/]",
               $"{SystemConstants.UiMarkup.BoldRed}CRITICAL WARNING:[/] This " +
               "operation will delete [bold]EVERY SINGLE[/] " +
               $"{entityNames} record from the {envName} environment."
            );

            warningTable.AddRow(
               "[bold red]![/]",
               $"{SystemConstants.UiMarkup.Red}This is NOT restricted to " +
               "test data. Real data will be destroyed.[/]"
            );

            warningTable.AddRow(
               "[bold red]![/]",
               $"{SystemConstants.UiMarkup.Red}This action is permanent and " +
               "irreversible.[/]"
            );

            AnsiConsole.Write(
               new Panel(warningTable)
                  .Header("[bold red] DANGER [/]")
                  .BorderColor(Color.Red)
            );
         }

         var wipeText = SystemConstants.UiMarkup.WipeDataConfirmation;
         var prompt =
            $"Type {SystemConstants.UiMarkup.BoldRed}{wipeText}[/] " +
            "to confirm:";

         var confirmation = AnsiConsole.Ask<string>(prompt);

         if (confirmation != SystemConstants.UiMarkup.WipeDataConfirmation)
         {
            CliUI.WriteWarning("Wipe cancelled.");

            return;
         }

         long remainingRecords = -1;
         long initialRecords = -1;
         var startTime = DateTime.Now;

         var progress = new Progress<long>(
            count =>
            {
               remainingRecords = count;
               if (initialRecords == -1)
                  initialRecords = count;
            }
         );

         await AnsiConsole.Status()
            .StartAsync(
               "Initializing wipe...",
               async ctx =>
               {
                  var cleanupTask = _wipeDataService.WipeEntitiesAsync(
                     provider,
                     selectedEntities,
                     progress,
                     ct
                  );

                  while (!cleanupTask.IsCompleted)
                  {
                     if (remainingRecords >= 0)
                     {
                        var elapsed = DateTime.Now - startTime;
                        var deleted = initialRecords - remainingRecords;
                        var etaStr = string.Empty;

                        if (deleted > 0 && elapsed.TotalSeconds > 5)
                        {
                           var recordsPerSec =
                              deleted / elapsed.TotalSeconds;

                           if (recordsPerSec > 0)
                           {
                              var remainingSeconds =
                                 remainingRecords / recordsPerSec;
                              var remainingTime =
                                 TimeSpan.FromSeconds(remainingSeconds);

                              etaStr =
                                 $" [grey]{remainingTime:hh\\:mm\\:ss}[/]";
                           }
                        }

                        ctx.Status(
                           $"[yellow]Wiping data...[/] " +
                           $"{remainingRecords} records remaining..." +
                           etaStr
                        );
                     }

                     await Task.Delay(500, ct);
                  }

                  await cleanupTask;
               }
            );

         CliUI.WriteSuccess($"Data Wipe Finished for {envName}!");
      }
   }
}


// --- Source: dvmig.Cli\Actions\SettingsActions.cs ---



namespace dvmig.Cli.Actions
{
   /// <summary>
   /// Handles application settings management from the CLI.
   /// </summary>
   public class SettingsActions(
      ISettingsService settingsService,
      ConnectionManager connectionManager
   )
   {
      private readonly ISettingsService _settingsService = settingsService;
      private readonly ConnectionManager _connectionManager = connectionManager;

      private enum SettingChoice
      {
         SourceConn,
         TargetConn,
         RememberConn,
         AutoConnect,
         MaxThreads,
         Language,
         Back
      }

      /// <summary>
      /// Displays the settings menu and handles user interaction.
      /// </summary>
      public async Task HandleSettingsMenuAsync(CancellationToken ct)
      {
         bool back = false;

         while (!back)
         {
            CliUI.WriteHeader();

            var settings = _settingsService.LoadSettings();

            var prompt = new SelectionPrompt<SettingChoice>()
               .Title("Settings".t())
               .PageSize(10)
               .UseConverter(c => c switch
               {
                  SettingChoice.SourceConn =>
                     $"{"Source Connection String".t()}: " + 
                     $"{StringMasker.GetEnvironmentUrl(
                        settings.SourceConnectionString)}",
                  SettingChoice.TargetConn =>
                     $"{"Target Connection String".t()}: " + 
                     $"{StringMasker.GetEnvironmentUrl(
                        settings.TargetConnectionString)}",
                  SettingChoice.RememberConn =>
                     $"{"Remember Connections".t()}: " + 
                     $"{(settings.RememberConnections ? "Yes".t() : "No".t())}",
                  SettingChoice.AutoConnect =>
                     $"{"Auto Connect".t()}: " + 
                     $"{(settings.AutoConnect ? "Yes".t() : "No".t())}",
                  SettingChoice.MaxThreads =>
                     $"{"Max Threads".t()}: {settings.MaxParallelism}",
                  SettingChoice.Language =>
                     $"{"Language".t()}: " +
                     $"{GetCurrentLanguageName(settings.Language)}",
                  SettingChoice.Back => "Back".t(),
                  _ => throw new ArgumentOutOfRangeException()
               })
               .AddChoices(Enum.GetValues<SettingChoice>());

            var choice = AnsiConsole.Prompt(prompt);

            switch (choice)
            {
               case SettingChoice.Back:
                  back = true;
                  break;
               case SettingChoice.Language:
                  await HandleLanguageChangeAsync(settings);
                  break;
               case SettingChoice.SourceConn:
                  await HandleConnectionStringChange(
                     settings, 
                     SystemConstants.ConnectionDirection.Source
                  );
                  break;
               case SettingChoice.TargetConn:
                  await HandleConnectionStringChange(
                     settings, 
                     SystemConstants.ConnectionDirection.Target
                  );
                  break;
               case SettingChoice.MaxThreads:
                  await HandleMaxParallelismChangeAsync(settings);
                  break;
               case SettingChoice.RememberConn:
                  settings.RememberConnections = !settings.RememberConnections;
                  _settingsService.SaveSettings(settings);
                  break;
               case SettingChoice.AutoConnect:
                  settings.AutoConnect = !settings.AutoConnect;
                  _settingsService.SaveSettings(settings);
                  break;
            }
         }
      }

      private static string GetCurrentLanguageName(string code)
      {
         return code.ToLowerInvariant() switch
         {
            "sv" => "Swedish".t(),
            _ => "English".t()
         };
      }

      private async Task HandleLanguageChangeAsync(UserSettings settings)
      {
         var prompt = new SelectionPrompt<string>()
            .Title("Select Language".t())
            .AddChoices(
               new[]
               {
                  "English".t(),
                  "Swedish".t()
               }
            );

         var choice = AnsiConsole.Prompt(prompt);
         var newLanguage = choice == "Swedish".t() ? "sv" : "en";

         if (settings.Language != newLanguage)
         {
            settings.Language = newLanguage;
            _settingsService.SaveSettings(settings);
            LocalizationService.Initialize(newLanguage);
            
            AnsiConsole.MarkupLine(
               $"{SystemConstants.UiMarkup.Green}" + 
               $"{"Settings updated.".t()}[/]"
            );

            await Task.Delay(1000);
         }
      }

      private async Task HandleMaxParallelismChangeAsync(UserSettings settings)
      {
         var maxThreads = AnsiConsole.Prompt(
            new SelectionPrompt<int>()
               .Title(
                  $"Select {SystemConstants.UiMarkup.Green}Max Parallelism[/]"
                  + $" ({"Threads".t()}):"
               )
               .AddChoices(SystemConstants.SyncSettings.ParallelismOptions)
         );

         if (settings.MaxParallelism != maxThreads)
         {
            settings.MaxParallelism = maxThreads;
            _settingsService.SaveSettings(settings);

            AnsiConsole.MarkupLine(
               $"{SystemConstants.UiMarkup.Green}" + 
               $"{"Settings updated.".t()}[/]"
            );

            await Task.Delay(1000);
         }
      }

      private async Task HandleConnectionStringChange(
         UserSettings settings, 
         SystemConstants.ConnectionDirection direction
      )
      {
         var label = direction == SystemConstants.ConnectionDirection.Source 
            ? "Source".t() 
            : "Target".t();
         
         var current = direction == SystemConstants.ConnectionDirection.Source
            ? settings.SourceConnectionString 
            : settings.TargetConnectionString;

         bool back = false;

         while (!back)
         {
            CliUI.WriteHeader();
            
            AnsiConsole.MarkupLine(
               $"[bold]{"Edit {0} Connection String".t(label)}[/]"
            );

            AnsiConsole.WriteLine();

            var prompt = new SelectionPrompt<string>()
               .AddChoices(
                  new[]
                  {
                     $"{"Connection String:".t()} " + 
                     $"{StringMasker.GetEnvironmentUrl(current)}",
                     "Test Connection".t(),
                     "Back".t()
                  }
               );

            var choice = AnsiConsole.Prompt(prompt);

            if (choice == "Back".t())
            {
               back = true;
            }
            else if (choice.StartsWith("Connection String:".t()))
            {
               var newConn = AnsiConsole.Prompt(
                  new TextPrompt<string>("Connection String:".t())
                     .DefaultValue(current)
                     .HideDefaultValue()
               );

               if (!string.IsNullOrWhiteSpace(newConn) && newConn != current)
               {
                  if (direction == SystemConstants.ConnectionDirection.Source)
                     settings.SourceConnectionString = newConn;
                  else
                     settings.TargetConnectionString = newConn;

                  _settingsService.SaveSettings(settings);
                  current = newConn;

                  AnsiConsole.MarkupLine(
                     $"{SystemConstants.UiMarkup.Green}" + 
                     $"{"Settings updated.".t()}[/]"
                  );
                  
                  await Task.Delay(1000);
               }
            }
            else if (choice == "Test Connection".t())
            {
               await HandleTestConnectionAsync(current, direction);
            }
         }
      }

      private async Task HandleTestConnectionAsync(
         string connStr, 
         SystemConstants.ConnectionDirection direction
      )
      {
         if (string.IsNullOrWhiteSpace(connStr))
            return;

         bool isLegacy = AnsiConsole.Confirm(
            "Is this a Legacy CRM (OnPrem) environment?".t(),
            false
         );

         IDataverseProvider? provider = await CliUI.RunStatusAsync(
            "Testing connection...".t(),
            async () =>
            {
               try
               {
                  IDataverseProvider p = isLegacy
                     ? new LegacyCrmProvider(connStr)
                     : new DataverseProvider(connStr);

                  await p.ExecuteAsync(new WhoAmIRequest(), default);

                  return p;
               }
               catch (Exception ex)
               {
                  CliUI.WriteError(
                     "Connection failed: {0}".t(ex.GetBaseException().Message)
                  );

                  return null;
               }
            }
         );

         if (provider != null)
         {
            _connectionManager.AddActiveConnection(direction, provider);

            CliUI.WriteSuccess("Connection successful!".t());
            await Task.Delay(1500);
         }
      }
   }
}


// --- Source: dvmig.Cli\Actions\SyncActions.cs ---

namespace dvmig.Cli.Actions
{
   public class SyncActions(
      ConnectionManager connectionManager,
      IPluginService pluginService,
      IValidationService validator,
      ISchemaService schemaService,
      ILogger logger,
      IEntityService entityService,
      ISettingsService settingsService
      ) : BaseActions(
         connectionManager,
         pluginService,
         validator,
         schemaService,
         logger,
         entityService,
         settingsService
      )
   {
      public async Task HandleSelectedSyncAsync(
         CancellationToken ct,
         bool forceResync = false
      )
      {
         var (source, target, engine, userResolver) =
            await SetupSyncEngineAsync();

         if (source == null || target == null || engine == null ||
             userResolver == null)
            return;

         var selectedEntities = await CliUI.SelectEntitiesAsync(
            EntityService,
            source
         );

         if (selectedEntities == null || selectedEntities.Count == 0)
         {
            CliUI.WriteWarning("No entities selected.".t());

            return;
         }

         if (!await ShowSyncPlanAsync(
            userResolver,
            selectedEntities,
            forceResync,
            ct
         ))
            return;

         await ExecuteSyncWorkflowAsync(
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
         var (source, target, engine, userResolver) =
            await SetupSyncEngineAsync();

         if (source == null || target == null || engine == null ||
             userResolver == null)
            return;

         var recommendedEntities = SyncSettings.RecommendedEntities.ToList();

         if (!await ShowSyncPlanAsync(
            userResolver,
            recommendedEntities,
            forceResync,
            ct
         ))
            return;

         await ExecuteSyncWorkflowAsync(
            engine,
            source,
            target,
            recommendedEntities,
            forceResync,
            ct
         );
      }

      private async Task<bool> ShowSyncPlanAsync(
         IUserService userResolver,
         List<string> entities,
         bool forceResync,
         CancellationToken ct
      )
      {
         if (!ConnectionManager.UserMappingsCached)
         {
            await CliUI.RunStatusAsync(
               "Mapping source users to target environment...".t(),
               Logger,
               async () => await userResolver.MapAllSourceUsersAsync(ct)
            );
            ConnectionManager.UserMappingsCached = true;
         }

         var mappings = await userResolver.GetMappingSummaryAsync(ct);
         var humanMappings = mappings.Where(m => m.IsHuman).ToList();
         var systemCount = mappings.Count - humanMappings.Count;

         var title = forceResync ? "Re-sync".t() : "Sync".t();

         var entityTable = new Table()
            .Border(TableBorder.Minimal)
            .AddColumn(
               new TableColumn($"{UiMarkup.BoldCyan}{"Seq".t()}[/]").Centered()
            )
            .AddColumn(
               new TableColumn($"{UiMarkup.BoldCyan}{"Entity".t()}[/]")
            );

         for (int i = 0; i < entities.Count; i++)
         {
            entityTable.AddRow(
               (i + 1).ToString(),
               $"{UiMarkup.Yellow}{entities[i]}[/]"
            );
         }

         var userTable = new Table()
            .Border(TableBorder.Minimal)
            .AddColumn($"{UiMarkup.BoldCyan}{"Source User".t()}[/]")
            .AddColumn($"{UiMarkup.BoldCyan}{"Target User".t()}[/]")
            .AddColumn(
               new TableColumn($"{UiMarkup.BoldCyan}{"Status".t()}[/]").Centered()
            );

         if (!humanMappings.Any())
         {
            userTable.AddRow(
               new Markup($"{UiMarkup.Grey}{"No human users found.".t()}[/]"),
               Text.Empty,
               Text.Empty
            );
         }
         else
         {
            foreach (var mapping in humanMappings)
            {
               var statusColor = mapping.Status == "Mapped" ? "green" : "yellow";

               userTable.AddRow(
                  mapping.SourceName,
                  mapping.TargetName,
                  $"[{statusColor}]{mapping.Status.t()}[/]"
               );
            }
         }

         var columns = new Columns(
            new Panel(entityTable).Header($"[bold]{"Entities".t()}[/]").Expand(),
            new Panel(userTable).Header($"[bold]{"User Mappings".t()}[/]").Expand()
         );

         AnsiConsole.Write(
            new Panel(columns)
               .Header($"[bold cyan] {title} {"Plan".t()} [/]")
               .Expand()
         );

         if (systemCount > 0)
         {
            AnsiConsole.MarkupLine(
               $"{UiMarkup.Grey}{"Note: {0} system accounts mapped automatically and hidden from this view.".t(systemCount)}[/]"
            );
         }

         AnsiConsole.WriteLine();

         if (!AnsiConsole.Confirm($"{"Proceed with this {0} plan?".t(title)}", true))
         {
            CliUI.WriteWarning($"{"{0} cancelled.".t(title)}");

            return false;
         }

         return true;
      }

      private async Task ExecuteSyncWorkflowAsync(
         ISyncEngine engine,
         IDataverseProvider source,
         IDataverseProvider target,
         List<string> entities,
         bool forceResync,
         CancellationToken ct
      )
      {
         var maxThreads = SettingsService.LoadSettings().MaxParallelism;

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
                  foreach (var rawLogicalName in entities)
                  {
                     var logicalName = rawLogicalName.ToLowerInvariant();

                     var actionTitle = forceResync 
                        ? "Re-syncing".t() 
                        : "Migrating".t();
                     var displayName = char.ToUpper(logicalName[0]) +
                        logicalName.Substring(1);

                     var sourceCountTask = source.GetRecordCountAsync(
                        logicalName,
                        ct
                     );

                     var targetCountTask = forceResync
                        ? Task.FromResult(0L)
                        : target.GetRecordCountAsync(
                           logicalName,
                           ct
                        );

                     await Task.WhenAll(sourceCountTask, targetCountTask);

                     long totalCount = await sourceCountTask;
                     long targetCount = await targetCountTask;

                     if (totalCount == 0)
                     {
                        AnsiConsole.MarkupLine(
                           $"{UiMarkup.Grey}{"No records found for {0}.".t(logicalName)}[/]"
                        );

                        continue;
                     }

                     var initialProcessed = (int)Math.Min(
                        totalCount,
                        targetCount
                     );

                     var task = ctx.AddTask(
                        "Initializing...".t(),
                        true,
                        totalCount
                     );

                     task.Value = initialProcessed;

                     var progressProvider = new MigrationProgressProvider(
                        task,
                        maxThreads,
                        actionTitle,
                        displayName,
                        totalCount,
                        initialProcessed
                     );

                     var options = new SyncOptions
                     {
                        StripMissingDependencies = true,
                        MaxDegreeOfParallelism = maxThreads,
                        ForceResync = forceResync,
                        PreserveAuditData = true
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
                           progressProvider.GetProgressReporter(),
                           ct
                        );
                     }
                     catch (Exception ex)
                     {
                        var baseEx = ex.GetBaseException();

                        CliUI.WriteError(
                           $"{"Sync aborted for {0}:".t(logicalName)} " +
                           $"{baseEx.Message}"
                        );
                     }
                     finally
                     {
                        Logger.DetachProgress();
                        progressProvider.FinalizeProgress();
                     }
                  }
               }
            );

         var actionName = forceResync ? "Re-sync".t() : "Migration".t();
         CliUI.WriteSuccess($"{"{0} Finished!".t(actionName)}");

         CliUI.Pause();
      }
   }
}


// --- Source: dvmig.Cli\CliUI.cs ---



namespace dvmig.Cli
{
   /// <summary>
   /// Encapsulates repetitive Spectre.Console UI patterns for the CLI.
   /// </summary>
   public static class CliUI
   {
      /// <summary>
      /// Represents an item in the main menu.
      /// </summary>
      /// <param name="Label">The display label.</param>
      /// <param name="Action">The action to execute.</param>
      public record MenuItem(
         string Label,
         Func<CancellationToken, Task>? Action
      );

      /// <summary>
      /// Runs an asynchronous task with a spinning status indicator and 
      /// provides a progress reporter that logs to the console.
      /// </summary>
      /// <param name="statusMessage">The message to display.</param>
      /// <param name="action">The asynchronous action to execute.</param>
      /// <param name="logger">The logger instance.</param>
      public static async Task RunStatusAsync(
         string statusMessage,
         ILogger logger,
         Func<Task> action
      )
      {
         await AnsiConsole.Status()
            .StartAsync(statusMessage, async ctx =>
            {
               var progress = new Progress<string>(msg =>
               {
                  bool isPersistent =
                     msg.StartsWith(SystemConstants.UiMarkup.Yellow) ||
                     msg.StartsWith(SystemConstants.UiMarkup.Red);

                  if (isPersistent)
                  {
                     AnsiConsole.MarkupLine(
                        $"{SystemConstants.UiMarkup.Grey}" +
                        $"[[{DateTime.Now:HH:mm:ss}]][/] {msg}"
                     );
                  }
                  else
                  {
                     ctx.Status(msg);
                  }
               });

               logger.AttachProgress(progress);

               try
               {
                  await action();
               }
               finally
               {
                  logger.DetachProgress();
               }
            });
      }

      /// <summary>
      /// Runs an asynchronous task with a spinning status indicator that 
      /// returns a value.
      /// </summary>
      /// <typeparam name="T">The return type.</typeparam>
      /// <param name="statusMessage">The message to display.</param>
      /// <param name="action">The asynchronous action to execute.</param>
      public static async Task<T> RunStatusAsync<T>(
         string statusMessage,
         Func<Task<T>> action
      )
      {

         return await AnsiConsole.Status()
            .StartAsync(statusMessage, async ctx => await action());
      }

      public static void WriteHeader()
      {
         AnsiConsole.Clear();
         AnsiConsole.Write(new FigletText("DVMIG").Color(Color.Blue));

         var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "Unknown";

         if (version.Contains('+'))
            version = version[..version.IndexOf('+')];

         AnsiConsole.MarkupLine(
            $"[bold]Dataverse Migrator (v{version})[/]"
         );

         AnsiConsole.WriteLine();
      }

      public static void WriteSuccess(string message)
      {
         AnsiConsole.MarkupLine(
            $"{SystemConstants.UiMarkup.BoldGreen}{message}[/]"
         );
      }

      public static void WriteWarning(string message)
      {
         AnsiConsole.MarkupLine(
            $"{SystemConstants.UiMarkup.Yellow}{message}[/]"
         );
      }

      public static void WriteError(string message)
      {
         AnsiConsole.MarkupLine(
            $"{SystemConstants.UiMarkup.Red}{message}[/]"
         );
      }

      public static void Pause()
      {
         AnsiConsole.WriteLine();
         AnsiConsole.MarkupLine(
            $"{SystemConstants.UiMarkup.Grey}" +
            "Press any key to return to menu...".t() + "[/]"
         );

         Console.ReadKey(true);
      }

      public static bool Confirm(string message, bool defaultValue = true)
      {
         return AnsiConsole.Confirm(message, defaultValue);
      }

      public static MenuItem PromptMainMenu(
         SyncActions syncActions,
         MaintenanceActions maintenanceActions,
         SettingsActions settingsActions,
         Action onExit
      )
      {
         var mainPrompt = new SelectionPrompt<MenuItem>()
            .Title("What would you like to do?".t())
            .PageSize(15)
            .UseConverter(m => m.Label)
            .HighlightStyle(new Style
            {
               Foreground = Color.MediumOrchid
            });

         var syncGroup = new List<MenuItem>
         {
            new(
               "Sync Recommended".t(),
               ct => syncActions.HandleRecommendedSyncAsync(ct, false)
            ),
            new(
               $"{"Sync Selected".t()} {SystemConstants.UiMarkup.Grey}" +
               $"({"pick entities".t()})[/]",
               ct => syncActions.HandleSelectedSyncAsync(ct, false)
            ),
            new(
               "Re-sync Recommended".t(),
               ct => syncActions.HandleRecommendedSyncAsync(ct, true)
            ),
            new(
               $"{"Re-sync Selected".t()} {SystemConstants.UiMarkup.Grey}" +
               $"({"pick entities".t()})[/]",
               ct => syncActions.HandleSelectedSyncAsync(ct, true)
            )
         };
         
         var maintenanceGroup = new List<MenuItem>
            {
               new(
                  $"{"Install DVMig Components".t()} " +
                  $"{SystemConstants.UiMarkup.Grey}({"Target".t()})[/]",
                  maintenanceActions.HandleInstallMenuAsync
               ),
               new(
                  $"{"Uninstall DVMig Components".t()} " +
                  $"{SystemConstants.UiMarkup.Grey}({"Target".t()})[/]",
                  maintenanceActions.HandleTargetComponentsCleanupAsync
               ),
               new(
                  "View Recorded Migration Failures".t(),
                  maintenanceActions.HandleViewFailuresAsync
               ),
            };

         var dataGroup = new List<MenuItem> {
               new(
                  $"{"Generate Sample Data".t()} " +
                  $"{SystemConstants.UiMarkup.Grey}({"Source".t()})[/]",
                  maintenanceActions.HandleSeedingAsync
               ),
               new(
                  $"{"Wipe Data on Source".t()} " +
                  $"{SystemConstants.UiMarkup.Grey}({"Caution!".t()})[/]",
                  maintenanceActions.HandleSourceDataCleanupAsync
               ),
               new(
                  $"{"Wipe Data on Target".t()} " +
                  $"{SystemConstants.UiMarkup.Grey}({"Caution!".t()})[/]",
                  maintenanceActions.HandleTargetDataCleanupAsync
               )
            };

         mainPrompt.AddChoiceGroup(
            new MenuItem(
               $"🚀 {SystemConstants.UiMarkup.BoldGreen}" + 
               "Synchronization".t() + "[/]",
               null
            ),
            syncGroup
         );

         mainPrompt.AddChoiceGroup(
            new MenuItem(
               $"🛠️ {SystemConstants.UiMarkup.BoldCyan}" + 
               "Maintenance".t() + "[/]",
               null
            ),
            maintenanceGroup
         );

         mainPrompt.AddChoiceGroup(
            new MenuItem(
               $"🧪 {SystemConstants.UiMarkup.BoldMagenta}" +
               "Data Management".t() + "[/]",
               null
            ),
            dataGroup
         );

         mainPrompt.AddChoices(
            new[]
            {
               new MenuItem(
                  "Settings".t(),
                  settingsActions.HandleSettingsMenuAsync
               ),
               new MenuItem(
                  "Exit".t(),
                  (ct) =>
                  {
                     onExit();

                     return Task.CompletedTask;
                  }
               )
            }
         );

         return AnsiConsole.Prompt(mainPrompt);
      }

      public static async Task<List<string>?> SelectEntitiesAsync(
         IEntityService entityService,
         IDataverseProvider provider
      )
      {
         var entities = await RunStatusAsync(
            "Fetching entity metadata...".t(),
            async () =>
            {
               try
               {
                  return await entityService
                     .GetMigrationEntitiesAsync(
                        provider,
                        default
                     );
               }
               catch (Exception ex)
               {
                  WriteError(
                     $"Failed to fetch metadata: {ex.Message}"
                  );

                  return null;
               }
            }
         );

         if (entities == null || entities.Count == 0)
            return null;

         var prompt = new MultiSelectionPrompt<string>()
            .Title(
               $"Select {SystemConstants.UiMarkup.Green}Entities[/] " +
               "to migrate:"
            )
            .PageSize(15)
            .MoreChoicesText(
               $"{SystemConstants.UiMarkup.Grey}" +
               "(Move up and down to reveal more)[/]"
            )
            .InstructionsText(
               $"{SystemConstants.UiMarkup.Grey}(Press " +
               $"{SystemConstants.UiMarkup.Blue}<space>[/] to toggle, " +
               $"{SystemConstants.UiMarkup.Green}<enter>[/] to accept)[/]"
            );

         foreach (var entity in entities)
            prompt.AddChoice(entity.DisplayName.UserLocalizedLabel.Label);

         return AnsiConsole.Prompt(prompt);
      }
   }
}


// --- Source: dvmig.Cli\ConnectionManager.cs ---




namespace dvmig.Cli
{
   public class ConnectionManager
   {
      private readonly ISettingsService _settingsService;

      private readonly Dictionary<ConnectionDirection, IDataverseProvider>
         _activeConnections = new();

      public bool UserMappingsCached { get; set; } = false;
      public IUserService? UserResolver { get; set; }

      public ConnectionManager(ISettingsService settingsService)
      {
         _settingsService = settingsService;
      }

      public void AddActiveConnection(
         ConnectionDirection direction, 
         IDataverseProvider provider
      )
      {
         _activeConnections[direction] = provider;
      }

      public async Task<IDataverseProvider?> ConnectAsync(
         ConnectionDirection direction,
         string? label = null
      )
      {
         label ??= direction.ToString();

         if (_activeConnections.TryGetValue(direction, out var existing))
         {
            var reuse = AnsiConsole.Confirm(
               $"Reuse active connection to {UiMarkup.Green}{label}[/]?",
               true
            );

            if (reuse)
               return existing;

            _activeConnections.Remove(direction);
         }

         var settings = _settingsService.LoadSettings();
         string? storedConn = direction == ConnectionDirection.Source
            ? settings.SourceConnectionString
            : settings.TargetConnectionString;

         string connStr = storedConn;
         bool isLegacy = false;

         if (!string.IsNullOrEmpty(storedConn))
         {
            var preview = StringMasker.MaskConnectionString(storedConn);
            var useStored = AnsiConsole.Confirm(
               $"Use {UiMarkup.Green}stored[/] {label} connection?",
               true
            );

            if (!useStored)
            {
               connStr = AnsiConsole.Ask<string>(
                  $"Enter {UiMarkup.BoldBlue}{label}[/] " +
                  "Connection String:"
               );

               isLegacy = AnsiConsole.Confirm(
                  $"Is {UiMarkup.BoldBlue}{label}[/] Legacy CRM " +
                  "(OnPrem)?",
                  false
               );
            }
         }
         else
         {
            connStr = AnsiConsole.Ask<string>(
               $"Enter {UiMarkup.BoldBlue}{label}[/] " +
               "Connection String:"
            );

            isLegacy = AnsiConsole.Confirm(
               $"Is {UiMarkup.BoldBlue}{label}[/] Legacy CRM " +
               "(OnPrem)?",
               false
            );
         }

         IDataverseProvider? provider = await CliUI.RunStatusAsync(
            $"Connecting to {label}...",
            async () =>
            {
               try
               {
                  IDataverseProvider p = isLegacy
                     ? new LegacyCrmProvider(connStr)
                     : new DataverseProvider(connStr);

                  await p.ExecuteAsync(new WhoAmIRequest(), default);

                  return p;
               }
               catch (Exception ex)
               {
                  AnsiConsole.MarkupLine(
                     $"{UiMarkup.Red}×[/] Failed to " +
                     $"connect to {label}: {ex.Message}"
                  );

                  return null;
               }
            }
         );

         if (provider != null)
         {
            _activeConnections[direction] = provider;

            // Extract organization URL for a cleaner success message
            var displayInfo = connStr.Contains("://")
               ? connStr.Split("://")[1].Split(";")[0].Split("/")[0]
               : label;

            AnsiConsole.MarkupLine(
               $"{UiMarkup.BoldGreen}✓[/] {label} Connected: " +
               $"{UiMarkup.Grey}{displayInfo}[/]"
            );

            if (connStr != storedConn)
            {
               if (AnsiConsole.Confirm(
                  $"Save this {label} connection string?",
                  true
               ))
               {
                  settings.RememberConnections = true;

                  if (direction == ConnectionDirection.Source)
                     settings.SourceConnectionString = connStr;
                  else
                     settings.TargetConnectionString = connStr;

                  _settingsService.SaveSettings(settings);
               }
            }
         }

         return provider;
      }
   }
}


// --- Source: dvmig.Cli\Program.cs ---


namespace dvmig.Cli
{
   class Program
   {
      private static IServiceProvider? _serviceProvider;
      private static CancellationTokenSource? _currentActionCts;
      private static DateTime _lastCtrlC = DateTime.MinValue;

      static async Task Main(string[] args)
      {
         InitializeDependencyInjection();
         InitializeLocalization();
         InitializeConsole(args);

         await HandleMenuActions();
      }

      private static void InitializeLocalization()
      {
         var settingsService = _serviceProvider?.GetService<ISettingsService>();
         var settings = settingsService?.LoadSettings();
         LocalizationService.Initialize(settings?.Language ?? "en");
      }

      private static void InitializeDependencyInjection()
      {
         var services = new ServiceCollection();

         // Shared Infrastructure
         services.AddSingleton<ILogger, Logger>();
         services.AddSingleton<ISettingsService, SettingsService>();
         services.AddSingleton<ConnectionManager>();
         services.AddSingleton<ISyncStateService, SyncStateService>();

         // Provisioning & Metadata
         services.AddTransient<IUserService, UserService>();
         services.AddTransient<ISeedingService, SeedingService>();
         services.AddTransient<IWipeDataService, WipeDataService>();
         services.AddTransient<IEntityService, EntityService>();

         services.AddTransient<IValidationService, ValidationService>();
         services.AddTransient<ISchemaService, SchemaService>();
         services.AddTransient<IPluginService, PluginService>();

         // CLI Actions
         services.AddTransient<SyncActions>();
         services.AddTransient<MaintenanceActions>();
         services.AddTransient<SettingsActions>();

         _serviceProvider = services.BuildServiceProvider();
      }

      private static void InitializeConsole(string[] args)
      {
         Console.OutputEncoding = Encoding.UTF8;

         Console.CancelKeyPress += (s, e) =>
         {
            e.Cancel = true;

            var now = DateTime.Now;

            if (now - _lastCtrlC < TimeSpan.FromSeconds(1))
               Environment.Exit(0);

            _lastCtrlC = now;

            _currentActionCts?.Cancel();
         };
      }

      private static async Task HandleMenuActions()
      {
         CliUI.WriteHeader();

         if (_serviceProvider == null)
         {
            CliUI.WriteError("Service provider is not initialized.");
            
            return;
         }

         var syncActions = _serviceProvider.GetRequiredService<SyncActions>();
         var maintenanceActions = _serviceProvider
            .GetRequiredService<MaintenanceActions>();
         var settingsActions = _serviceProvider
            .GetRequiredService<SettingsActions>();

         bool exit = false;

         while (!exit)
         {
            CliUI.MenuItem choice;

            try
            {
               choice = CliUI.PromptMainMenu(
                  syncActions,
                  maintenanceActions,
                  settingsActions,
                  () => exit = true
               );
            }
            catch(Exception)
            {
               // Handles cases where the prompt is interrupted (e.g., Ctrl+C),
               // preventing a crash and allowing the user to stay in the app.
               if (!CliUI.Confirm("Back (Y) or Quit (N)?", true))
                  exit = true;

               continue;
            }

            if (choice.Action != null)
            {
               _currentActionCts = new CancellationTokenSource();

               try
               {
                  await choice.Action(_currentActionCts.Token);
               }
               catch(OperationCanceledException)
               {
                  CliUI.WriteWarning("\nOperation interrupted.");

                  if (!CliUI.Confirm("Back (Y) or Quit (N)?", true))
                     exit = true;
               }
               catch(Exception ex)
               {
                  CliUI.WriteError($"An unexpected error occurred: "
                     + $"{ex.Message}");
               }
               finally
               {
                  _currentActionCts.Dispose();
                  _currentActionCts = null;
               }
            }

            if (!exit)
            {
               CliUI.WriteHeader();
            }
         }
      }
   }
}


// --- Source: dvmig.Cli\Providers\MigrationProgressProvider.cs ---

namespace dvmig.Cli.Providers
{
   /// <summary>
   /// Provides a bridge between the core migration progress and the 
   /// Spectre.Console UI, handling rate calculations and markup formatting.
   /// </summary>
   public class MigrationProgressProvider
   {
      private readonly ProgressTask _task;
      private readonly int _maxThreads;
      private readonly string _actionTitle;
      private readonly string _displayName;
      private readonly long _totalCount;
      private readonly object _progressLock = new object();
      private readonly System.Diagnostics.Stopwatch _sw;

      private int _processed;
      private int _failedCount;
      private DateTime _lastUpdate = DateTime.MinValue;

      /// <summary>
      /// Initializes a new instance of the 
      /// <see cref="MigrationProgressProvider"/> class.
      /// </summary>
      public MigrationProgressProvider(
         ProgressTask task,
         int maxThreads,
         string actionTitle,
         string displayName,
         long totalCount,
         int initialProcessed = 0
      )
      {
         _task = task;
         _maxThreads = maxThreads;
         _actionTitle = actionTitle;
         _displayName = displayName;
         _totalCount = totalCount;
         _processed = initialProcessed;

         _sw = System.Diagnostics.Stopwatch.StartNew();

         UpdateDescription(0);
      }

      /// <summary>
      /// Gets an <see cref="IProgress{T}"/> instance that updates the UI.
      /// </summary>
      public IProgress<bool> GetProgressReporter()
      {
         return new Progress<bool>(HandleProgress);
      }

      /// <summary>
      /// Finalizes the progress task with a final rate calculation.
      /// </summary>
      public void FinalizeProgress()
      {
         var finalElapsed = _sw.Elapsed.TotalSeconds;
         var finalRate = _processed / (finalElapsed > 0 ? finalElapsed : 1);

         UpdateDescription(finalRate);

         _task.Value = _totalCount;
         _task.StopTask();
      }

      private void HandleProgress(bool success)
      {
         var currentProcessed = Interlocked.Increment(ref _processed);

         if (!success)
            Interlocked.Increment(ref _failedCount);

         lock (_progressLock)
         {
            var now = DateTime.Now;

            if (now - _lastUpdate < TimeSpan.FromSeconds(1) &&
                currentProcessed < _totalCount)
               return;

            _lastUpdate = now;
            _task.Value = currentProcessed;

            var swElapsed = _sw.Elapsed.TotalSeconds;
            var recsPerSec = currentProcessed / (swElapsed > 0 ? swElapsed : 1);

            UpdateDescription(recsPerSec);
         }
      }

      private void UpdateDescription(double rate)
      {
         _task.Description = GetDesc(
            _processed,
            _totalCount,
            rate,
            _failedCount,
            _maxThreads,
            _actionTitle,
            _displayName
         );
      }

      private static string GetDesc(
         int p,
         long t,
         double r,
         int f,
         int maxThreads,
         string actionTitle,
         string displayName
      )
      {
         var titleMarkup = $"{SystemConstants.UiMarkup.BoldRed}{actionTitle} " +
            $"{displayName}[/]";

         var rateInfo = r > 0 ? $" - {r:F1} r/s" : "";
         var desc = $"{titleMarkup} ({p}/{t}) " +
            $"[[{SystemConstants.UiMarkup.Green}{maxThreads}t{rateInfo}[/]]] ";

         if (f > 0)
            desc += $"{SystemConstants.UiMarkup.Red}({f} failed)[/]";

         return desc;
      }
   }
}


// --- Source: dvmig.Core\GlobalSuppressions.cs ---
// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.


[assembly: SuppressMessage(
   "Style",
   "IDE0305:Simplify collection initialization",
   Justification = "<Pending>",
   Scope = "member",
   Target = "~M:dvmig.Core.Synchronization.ReconciliationService." +
            "GetFailuresAsync(dvmig.Core.Interfaces.IDataverseProvider," +
            "System.Threading.CancellationToken)~System.Threading.Tasks.Task" +
            "{System.Collections.Generic.List{dvmig.Core.Synchronization." +
            "MigrationFailureRecord}}"
)]


// --- Source: dvmig.Core\Interfaces\IDataverseProvider.cs ---

namespace dvmig.Core.Interfaces
{
   /// <summary>
   /// Interface for a Dataverse provider, abstracting CRUD and metadata
   /// operations for different versions of the Dataverse/CRM SDK.
   /// </summary>
   public interface IDataverseProvider
   {
      /// <summary>
      /// Gets the connection string used by this provider.
      /// </summary>
      string ConnectionString { get; }

      /// <summary>
      /// Retrieves a single entity record by ID.
      /// </summary>
      /// <param name="entityLogicalName">
      /// The logical name of the entity.
      /// </param>
      /// <param name="id">The record ID.</param>
      /// <param name="columns">Optional list of columns to retrieve.</param>
      /// <param name="ct">A cancellation token.</param>
      /// <returns>The retrieved entity, or null if not found.</returns>
      Task<Entity?> RetrieveAsync(
         string entityLogicalName,
         Guid id,
         string[]? columns = null,
         CancellationToken ct = default
      );

      /// <summary>
      /// Retrieves metadata for a specific entity.
      /// </summary>
      /// <param name="entityLogicalName">
      /// The logical name of the entity.
      /// </param>
      /// <param name="ct">A cancellation token.</param>
      /// <returns>The entity metadata, or null if retrieval fails.</returns>
      Task<EntityMetadata?> GetEntityMetadataAsync(
         string entityLogicalName,
         CancellationToken ct = default
      );

      /// <summary>
      /// Creates a new entity record.
      /// </summary>
      /// <param name="entity">The entity to create.</param>
      /// <param name="ct">A cancellation token.</param>
      /// <param name="callerId">
      /// Optional ID of the user to impersonate for this operation.
      /// </param>
      /// <returns>The ID of the newly created record.</returns>
      Task<Guid> CreateAsync(
         Entity entity,
         CancellationToken ct = default,
         Guid? callerId = null
      );

      /// <summary>
      /// Updates an existing entity record.
      /// </summary>
      /// <param name="entity">The entity containing the updates.</param>
      /// <param name="ct">A cancellation token.</param>
      /// <param name="callerId">
      /// Optional ID of the user to impersonate for this operation.
      /// </param>
      Task UpdateAsync(
         Entity entity,
         CancellationToken ct = default,
         Guid? callerId = null
      );

      /// <summary>
      /// Deletes an entity record.
      /// </summary>
      /// <param name="entityLogicalName">
      /// The logical name of the entity.
      /// </param>
      /// <param name="id">The ID of the record to delete.</param>
      /// <param name="ct">A cancellation token.</param>
      Task DeleteAsync(
         string entityLogicalName,
         Guid id,
         CancellationToken ct = default
      );

      /// <summary>
      /// Associates records in an N:N relationship.
      /// </summary>
      /// <param name="entityLogicalName">
      /// The logical name of the entity.
      /// </param>
      /// <param name="entityId">The ID of the target record.</param>
      /// <param name="relationship">The relationship definition.</param>
      /// <param name="relatedEntities">
      /// The collection of related entities.
      /// </param>
      /// <param name="ct">A cancellation token.</param>
      Task AssociateAsync(
         string entityLogicalName,
         Guid entityId,
         Relationship relationship,
         EntityReferenceCollection relatedEntities,
         CancellationToken ct = default
      );

      /// <summary>
      /// Executes a query and returns a collection of entities.
      /// </summary>
      /// <param name="query">The query to execute.</param>
      /// <param name="ct">A cancellation token.</param>
      /// <returns>The resulting entity collection.</returns>
      Task<EntityCollection> RetrieveMultipleAsync(
         QueryBase query,
         CancellationToken ct = default
      );

      /// <summary>
      /// Executes an organization request.
      /// </summary>
      /// <param name="request">The request to execute.</param>
      /// <param name="ct">A cancellation token.</param>
      /// <param name="callerId">
      /// Optional ID of the user to impersonate for this operation.
      /// </param>
      /// <returns>The organization response.</returns>
      Task<OrganizationResponse> ExecuteAsync(
         OrganizationRequest request,
         CancellationToken ct = default,
         Guid? callerId = null
      );
   }
}


// --- Source: dvmig.Core\Interfaces\IEntityService.cs ---

namespace dvmig.Core.Interfaces
{
   /// <summary>
   /// Defines the contract for a service that prepares entities
   /// for synchronization to the target environment, handles metadata,
   /// and record associations.
   /// </summary>
   public interface IEntityService
   {
      #region Preparation & Mapping

      /// <summary>
      /// Prepares a source entity for creation or update in the target 
      /// environment.
      /// </summary>
      Task<Entity> PrepareEntityForTargetAsync(
         Entity sourceEntity,
         EntityMetadata metadata,
         SyncOptions options,
         IUserService userResolver,
         ConcurrentDictionary<string, Guid> idMappingCache,
         CancellationToken ct = default
      );

      /// <summary>
      /// Attempts to find a matching record on the target environment.
      /// </summary>
      Task<Guid?> FindExistingOnTargetAsync(
         Entity entity,
         IDataverseProvider target,
         CancellationToken ct = default
      );

      /// <summary>
      /// Retrieves all record IDs for a specific entity from a provider.
      /// </summary>
      Task<HashSet<Guid>> GetAllIdsAsync(
         IDataverseProvider provider,
         string logicalName,
         CancellationToken ct = default
      );

      #endregion

      #region Metadata & Querying

      /// <summary>
      /// Retrieves metadata for a specific entity type, using a cache 
      /// if possible.
      /// </summary>
      Task<EntityMetadata?> GetMetadataAsync(
         string entityLogicalName,
         CancellationToken ct = default
      );

      /// <summary>
      /// Generates a ColumnSet containing only the attributes that are valid
      /// to retrieve for synchronization.
      /// </summary>
      Task<ColumnSet> GetValidColumnsAsync(
         string logicalName,
         CancellationToken ct = default
      );

      /// <summary>
      /// Retrieves a list of all entities that are suitable for migration.
      /// </summary>
      Task<List<EntityMetadata>> GetMigrationEntitiesAsync(
         IDataverseProvider provider,
         CancellationToken ct = default
      );

      /// <summary>
      /// Clears the internal metadata cache.
      /// </summary>
      void ClearMetadataCache();

      #endregion

      #region Relationships (N:N)

      /// <summary>
      /// Associates an N:N intersect record in the target environment.
      /// </summary>
      Task AssociateAsync(
         IDataverseProvider target,
         Entity entity,
         CancellationToken ct = default,
         Guid? callerId = null
      );

      #endregion
   }
}


// --- Source: dvmig.Core\Interfaces\ILogger.cs ---
namespace dvmig.Core.Interfaces
{
   /// <summary>
   /// Defines the contract for a logger that supports both standard logging
   /// and progress reporting to the user interface.
   /// </summary>
   public interface ILogger
   {
      /// <summary>
      /// Logs a debug-level message.
      /// </summary>
      /// <param name="message">The message to log.</param>
      void Debug(string message);

      /// <summary>
      /// Logs a debug-level message using a template.
      /// </summary>
      /// <param name="messageTemplate">The message template.</param>
      /// <param name="propertyValues">The values to format.</param>
      void Debug(string messageTemplate, params object[] propertyValues);

      /// <summary>
      /// Logs an information-level message.
      /// </summary>
      /// <param name="message">The message to log.</param>
      void Information(string message);

      /// <summary>
      /// Logs an information-level message using a template.
      /// </summary>
      /// <param name="messageTemplate">The message template.</param>
      /// <param name="propertyValues">The values to format.</param>
      void Information(string messageTemplate, params object[] propertyValues);

      /// <summary>
      /// Logs a warning-level message.
      /// </summary>
      /// <param name="message">The message to log.</param>
      void Warning(string message);

      /// <summary>
      /// Logs a warning-level message using a template.
      /// </summary>
      /// <param name="messageTemplate">The message template.</param>
      /// <param name="propertyValues">The values to format.</param>
      void Warning(string messageTemplate, params object[] propertyValues);

      /// <summary>
      /// Logs a warning-level message with an exception.
      /// </summary>
      /// <param name="ex">The exception to log.</param>
      /// <param name="messageTemplate">The message template.</param>
      /// <param name="propertyValues">The values to format.</param>
      void Warning(
         Exception ex,
         string messageTemplate,
         params object[] propertyValues
      );

      /// <summary>
      /// Logs an error-level message.
      /// </summary>
      /// <param name="message">The message to log.</param>
      void Error(string message);

      /// <summary>
      /// Logs an error-level message using a template.
      /// </summary>
      /// <param name="messageTemplate">The message template.</param>
      /// <param name="propertyValues">The values to format.</param>
      void Error(string messageTemplate, params object[] propertyValues);

      /// <summary>
      /// Logs an error-level message with an exception.
      /// </summary>
      /// <param name="ex">The exception to log.</param>
      /// <param name="messageTemplate">The message template.</param>
      /// <param name="propertyValues">The values to format.</param>
      void Error(
         Exception ex,
         string messageTemplate,
         params object[] propertyValues
      );

      /// <summary>
      /// Attaches a progress reporter to the logger.
      /// </summary>
      /// <param name="progress">The progress reporter.</param>
      void AttachProgress(IProgress<string> progress);

      /// <summary>
      /// Detaches the progress reporter from the logger.
      /// </summary>
      void DetachProgress();
   }
}


// --- Source: dvmig.Core\Interfaces\ISchemaService.cs ---
namespace dvmig.Core.Interfaces
{
   /// <summary>
   /// Defines the contract for managing schema creation in the target
   /// environment.
   /// </summary>
   public interface ISchemaService
   {
      /// <summary>
      /// Creates the 'dm_sourcedata' entity schema and its required 
      /// attributes in the target environment if they do not already exist.
      /// </summary>
      /// <param name="target">The target Dataverse provider.</param>
      /// <param name="progress">An optional progress reporter.</param>
      /// <param name="ct">A cancellation token.</param>
      /// <returns>A task representing the asynchronous operation.</returns>
      Task CreateSchemaAsync(
         IDataverseProvider target,
         CancellationToken ct = default
      );

      /// <summary>
      /// Removes the 'dm_sourcedata entity and all its data from 
      /// the target environment.
      /// </summary>
      /// <param name="target">The target Dataverse provider.</param>
      /// <param name="progress">An optional progress reporter.</param>
      /// <param name="ct">A cancellation token.</param>
      /// <returns>
      /// A task representing the asynchronous removal operation.
      /// </returns>
      Task DropSchemaAsync(
         IDataverseProvider target,
         CancellationToken ct = default
      );
   }
}


// --- Source: dvmig.Core\Interfaces\ISeedingService.cs ---
namespace dvmig.Core.Interfaces
{
   /// <summary>
   /// Defines the contract for a service that seeds and cleans test data in
   /// a Dataverse environment.
   /// </summary>
   public interface ISeedingService
   {
      /// <summary>
      /// Seeds sample data into the specified environment.
      /// </summary>
      /// <param name="provider">The Dataverse provider.</param>
      /// <param name="recordCount">The number of records per entity.</param>
      /// <param name="ct">A cancellation token.</param>
      /// <returns>A task representing the asynchronous operation.</returns>
      Task SeedSampleDataAsync(
         IDataverseProvider provider,
         int recordCount = 50,
         CancellationToken ct = default
      );
   }
}


// --- Source: dvmig.Core\Interfaces\ISettingsService.cs ---

namespace dvmig.Core.Interfaces
{
   /// <summary>
   /// Service interface for loading and saving application settings.
   /// </summary>
   public interface ISettingsService
   {
      /// <summary>
      /// Loads the user settings from persistent storage.
      /// </summary>
      /// <returns>The loaded user settings.</returns>
      UserSettings LoadSettings();

      /// <summary>
      /// Saves the specified user settings to persistent storage.
      /// </summary>
      /// <param name="settings">The settings to save.</param>
      void SaveSettings(UserSettings settings);
   }
}


// --- Source: dvmig.Core\Interfaces\ISyncEngine.cs ---

namespace dvmig.Core.Interfaces
{
   /// <summary>
   /// Defines the contract for an engine that synchronizes data records 
   /// between a source and target Dataverse environment.
   /// </summary>
   public interface ISyncEngine
   {
      #region Core Sync Operations

      /// <summary>
      /// Synchronizes all records of a specific entity type from the 
      /// source to the target, handling pagination automatically.
      /// </summary>
      Task SyncAsync(
         string logicalName,
         SyncOptions options,
         QueryExpression? query = null,
         IProgress<bool>? recordProgress = null,
         CancellationToken ct = default
      );

      /// <summary>
      /// Initializes the sync engine for a specific entity type.
      /// </summary>
      Task InitializeEntitySyncAsync(
         string logicalName,
         CancellationToken ct = default
      );

      /// <summary>
      /// Synchronizes a single entity record and reports success or failure 
      /// via the progress reporter.
      /// </summary>
      Task SyncRecordAndReportAsync(
         Entity entity,
         SyncOptions options,
         IProgress<bool>? recordProgress,
         CancellationToken ct = default
      );

      /// <summary>
      /// Synchronizes a single entity record to the target environment.
      /// </summary>
      Task<(bool Success, string? FailureMessage)> SyncRecordAsync(
         Entity entity,
         SyncOptions options,
         CancellationToken ct = default
      );

      /// <summary>
      /// Finds an existing record on the target environment.
      /// </summary>
      Task<Guid?> FindExistingOnTargetAsync(
         Entity entity,
         CancellationToken ct = default
      );

      #endregion

      #region Failure Management

      /// <summary>
      /// Logs a migration failure directly to the target environment.
      /// </summary>
      Task LogFailureToTargetAsync(
         Entity entity,
         string errorMessage,
         CancellationToken ct = default
      );

      /// <summary>
      /// Retrieves recorded migration failures from the target environment.
      /// </summary>
      Task<List<MigrationFailureRecord>> GetFailuresAsync(
         IDataverseProvider target,
         string? entityLogicalName = null,
         CancellationToken ct = default
      );

      /// <summary>
      /// Clears all recorded failures on the target environment.
      /// </summary>
      Task ClearFailuresAsync(
         IDataverseProvider target,
         CancellationToken ct = default
      );

      /// <summary>
      /// Checks if the target environment supports migration failure logging.
      /// </summary>
      Task<bool> IsFailureLoggingInitializedAsync(
         IDataverseProvider target,
         CancellationToken ct = default
      );

      #endregion
   }
}


// --- Source: dvmig.Core\Interfaces\ISyncStateService.cs ---

namespace dvmig.Core.Interfaces
{
   /// <summary>
   /// Manages state during the synchronization process, such as recursion 
   /// tracking, synced record IDs, and dependency tracking.
   /// </summary>
   public interface ISyncStateService
   {
      ConcurrentDictionary<string, HashSet<string>> TriedDependencies { get; }
      ConcurrentDictionary<string, Guid> IdMappingCache { get; }

      void InitializeSyncedIds(IEnumerable<Guid> ids);
      bool IsSynced(Guid id);
      void MarkAsSynced(Guid id);

      bool TryEnterRecordScope(string recordKey, int maxDepth);
      void LeaveRecordScope(string recordKey);

      void ClearState();
   }
}


// --- Source: dvmig.Core\Interfaces\IUserService.cs ---

namespace dvmig.Core.Interfaces
{
   /// <summary>
   /// Represents a summary of a user mapping from source to target.
   /// </summary>
   public record UserMappingSummary(
      string SourceName,
      Guid SourceId,
      string TargetName,
      Guid TargetId,
      string Status,
      bool IsHuman = false
   );

   /// <summary>
   /// Defines the contract for a service that resolves user references from 
   /// a source environment to the corresponding users in a target environment.
   /// </summary>
   public interface IUserService
   {
      /// <summary>
      /// Asynchronously resolves a source user reference to its equivalent 
      /// in the target environment.
      /// </summary>
      /// <param name="sourceUser">
      /// The user reference from the source environment.
      /// </param>
      /// <param name="ct">A cancellation token.</param>
      /// <returns>
      /// An entity reference to the resolved user in the target environment, 
      /// or null if resolution fails.
      /// </returns>
      Task<EntityReference?> MapUserAsync(
         EntityReference? sourceUser,
         CancellationToken ct = default
      );

      /// <summary>
      /// Manually adds a user mapping, bypassing automatic lookup.
      /// </summary>
      /// <param name="sourceUserId">The ID of the source user.</param>
      /// <param name="targetUserId">The ID of the target user.</param>
      void AddManualMapping(Guid sourceUserId, Guid targetUserId);

      /// <summary>
      /// Retrieves a summary of all user mappings resolved so far.
      /// </summary>
      /// <param name="ct">A cancellation token.</param>
      /// <returns>A list of user mapping summaries.</returns>
      Task<List<UserMappingSummary>> GetMappingSummaryAsync(
         CancellationToken ct = default
      );

      /// <summary>
      /// Proactively maps all active users from the source environment.
      /// </summary>
      /// <param name="ct">A cancellation token.</param>
      Task MapAllSourceUsersAsync(CancellationToken ct = default);

      /// <summary>
      /// Retrieves a list of active, human users from the specified provider.
      /// </summary>
      /// <param name="provider">The Dataverse provider to query.</param>
      /// <param name="ct">A cancellation token.</param>
      /// <returns>A list of user IDs.</returns>
      Task<List<Guid>> GetRealActiveUsersAsync(
         IDataverseProvider provider,
         CancellationToken ct = default
      );

      /// <summary>
      /// Clears the user mapping cache.
      /// </summary>
      void ClearCache();
   }
}


// --- Source: dvmig.Core\Interfaces\IValidationService.cs ---
namespace dvmig.Core.Interfaces
{
   /// <summary>
   /// Defines the contract for a service that validates environment 
   /// readiness and component versions.
   /// </summary>
   public interface IValidationService
   {
      /// <summary>
      /// Validates that the target environment is ready for migration.
      /// </summary>
      /// <param name="target">The target Dataverse provider.</param>
      /// <param name="ct">A cancellation token.</param>
      /// <returns>True if the environment is valid.</returns>
      Task<bool> ValidateTargetEnvironmentAsync(
         IDataverseProvider target,
         CancellationToken ct = default
      );
   }
}


// --- Source: dvmig.Core\Interfaces\IWipeDataService.cs ---
namespace dvmig.Core.Interfaces
{
   /// <summary>
   /// Defines the contract for a service that wipes data from a Dataverse 
   /// environment.
   /// </summary>
   public interface IWipeDataService
   {
      /// <summary>
      /// Wipes records of specified entities from the environment.
      /// </summary>
      /// <param name="provider">The Dataverse provider.</param>
      /// <param name="entities">Optional list of entities to clean.</param>
      /// <param name="progress">
      /// Optional progress reporter for remaining count.
      /// </param>
      /// <param name="ct">A cancellation token.</param>
      /// <returns>A task representing the asynchronous operation.</returns>
      Task WipeEntitiesAsync(
         IDataverseProvider provider,
         List<string>? entities = null,
         IProgress<long>? progress = null,
         CancellationToken ct = default
      );
   }
}


// --- Source: dvmig.Core\Providers\DataverseProvider.cs ---

namespace dvmig.Core.Providers
{
   /// <summary>
   /// Implementation of <see cref="IDataverseProvider"/> using the modern
   /// <see cref="ServiceClient"/> from the PowerPlatform.Dataverse.Client SDK.
   /// </summary>
   public class DataverseProvider : IDataverseProvider, IDisposable
   {
      private readonly ServiceClient _client;
      private readonly AsyncRetryPolicy _retryPolicy;

      /// <inheritdoc />
      public string ConnectionString { get; }

      /// <summary>
      /// Initializes a new instance of the 
      /// <see cref="DataverseProvider"/> class.
      /// </summary>
      /// <param name="connectionString">
      /// The connection string to the Dataverse environment.
      /// </param>
      public DataverseProvider(string connectionString)
      {
         ConnectionString = connectionString;
         _client = new ServiceClient(connectionString);

         if (!_client.IsReady)
            throw new Exception(
               $"Dataverse connection failed: {_client.LastError}"
            );

         _retryPolicy = Policy
            .Handle<Exception>(EntityHelper.IsTransientError)
            .WaitAndRetryAsync(
               5,
               attempt => TimeSpan.FromSeconds(
                  Math.Min(Math.Pow(2, attempt + 2), 30)
               )
            );
      }

      private async Task<T> ExecuteWithRetryAsync<T>(
         Func<CancellationToken, Task<T>> action,
         CancellationToken ct
      )
      {
         return await _retryPolicy.ExecuteAsync(
            async () => await action(ct)
         );
      }

      private async Task ExecuteWithRetryAsync(
         Func<CancellationToken, Task> action,
         CancellationToken ct
      )
      {
         await _retryPolicy.ExecuteAsync(
            async () => await action(ct)
         );
      }

      /// <inheritdoc />
      public async Task<Entity?> RetrieveAsync(
         string entityLogicalName,
         Guid id,
         string[]? columns = null,
         CancellationToken ct = default
      )
      {
         return await ExecuteWithRetryAsync(
            async (token) =>
            {
               try
               {
                  var columnSet = columns == null
                     ? new ColumnSet(true)
                     : new ColumnSet(columns);

                  return await _client.RetrieveAsync(
                     entityLogicalName,
                     id,
                     columnSet,
                     token
                  );
               }
               catch (Exception ex)
               {
                  if (ex.IsNotFoundException())
                     return null;

                  throw;
               }
            },
            ct
         );
      }

      /// <inheritdoc />
      public async Task<EntityMetadata?> GetEntityMetadataAsync(
         string entityLogicalName,
         CancellationToken ct = default
      )
      {
         return await ExecuteWithRetryAsync(
            async (token) =>
            {
               try
               {
                  var response = await _client.ExecuteAsync(
                     new RetrieveEntityRequest
                     {
                        LogicalName = entityLogicalName,
                        EntityFilters = EntityFilters.Attributes
                     },
                     token
                  ) as RetrieveEntityResponse;

                  return response?.EntityMetadata;
               }
               catch
               {
                  // If entity doesn't exist, RetrieveEntityRequest throws.
                  // We return null to indicate missing metadata.

                  return null;
               }
            },
            ct
         );
      }

      /// <inheritdoc />
      public async Task<Guid> CreateAsync(
         Entity entity,
         CancellationToken ct = default,
         Guid? callerId = null
      )
      {
         return await ExecuteWithRetryAsync(
            async (token) =>
            {
               if (callerId.HasValue && callerId.Value != Guid.Empty)
               {
                  clonedClient.CallerId = callerId.Value;

                  return await clonedClient.CreateAsync(entity, token);
               }

               return await _client.CreateAsync(entity, token);
            },
            ct
         );
      }

      /// <inheritdoc />
      public async Task UpdateAsync(
         Entity entity,
         CancellationToken ct = default,
         Guid? callerId = null
      )
      {
         await ExecuteWithRetryAsync(
            async (token) =>
            {
               if (callerId.HasValue && callerId.Value != Guid.Empty)
               {
                  clonedClient.CallerId = callerId.Value;
                  await clonedClient.UpdateAsync(entity, token);

                  return;
               }

               await _client.UpdateAsync(entity, token);
            },
            ct
         );
      }

      /// <inheritdoc />
      public async Task DeleteAsync(
         string entityLogicalName,
         Guid id,
         CancellationToken ct = default
      )
      {
         await ExecuteWithRetryAsync(
            async (token) =>
               await _client.DeleteAsync(entityLogicalName, id, token),
            ct
         );
      }

      /// <inheritdoc />
      public async Task AssociateAsync(
         string entityLogicalName,
         Guid entityId,
         Relationship relationship,
         EntityReferenceCollection relatedEntities,
         CancellationToken ct = default
      )
      {
         await ExecuteWithRetryAsync(
            async (token) =>
               await _client.AssociateAsync(
                  entityLogicalName,
                  entityId,
                  relationship,
                  relatedEntities,
                  token
               ),
            ct
         );
      }

      /// <inheritdoc />
      public async Task<EntityCollection> RetrieveMultipleAsync(
         QueryBase query,
         CancellationToken ct = default
      )
      {
         return await ExecuteWithRetryAsync(
            async (token) => await _client.RetrieveMultipleAsync(query, token),
            ct
         );
      }

      /// <inheritdoc />
      public async Task<OrganizationResponse> ExecuteAsync(
         OrganizationRequest request,
         CancellationToken ct = default,
         Guid? callerId = null
      )
      {
         return await ExecuteWithRetryAsync(
            async (token) =>
            {
               if (callerId.HasValue && callerId.Value != Guid.Empty)
               {
                  clonedClient.CallerId = callerId.Value;

                  return await clonedClient.ExecuteAsync(request, token);
               }

               return await _client.ExecuteAsync(request, token);
            },
            ct
         );
      }

      /// <summary>
      /// Disposes the underlying service client.
      /// </summary>
      public void Dispose()
      {
         _client.Dispose();
      }
   }
}


// --- Source: dvmig.Core\Providers\DataverseProviderExtensions.cs ---

namespace dvmig.Core.Providers
{
   /// <summary>
   /// Provides extension methods for <see cref="IDataverseProvider"/>.
   /// </summary>
   public static class DataverseProviderExtensions
   {
      /// <summary>
      /// Determines whether the specified exception indicates that 
      /// the requested record or object does not exist in Dataverse.
      /// </summary>
      /// <param name="ex">The exception to check.</param>
      /// <returns>True if it is a "Not Found" exception.</returns>
      public static bool IsNotFoundException(this Exception ex)
      {
         if (ex is not FaultException faultEx)
            return false;

         var doesNotExist = SystemConstants.ErrorCodes.DoesNotExist;
         var comparison = StringComparison.OrdinalIgnoreCase;

         return faultEx.Message.IndexOf(doesNotExist, comparison) >= 0 ||
                faultEx.Message.IndexOf(
                   SystemConstants.ErrorKeywords.DoesNotExist,
                   comparison
                ) >= 0;
      }

      /// <summary>
      /// Retrieves the primary ID attribute name for a given 
      /// entity logical name.
      /// </summary>
      /// <param name="provider">The Dataverse provider.</param>
      /// <param name="logicalName">The logical name of the entity.</param>
      /// <param name="ct">A cancellation token.</param>
      /// <returns>The primary ID attribute name, or null.</returns>
      public static async Task<string?> GetPrimaryIdAttributeAsync(
         this IDataverseProvider provider,
         string logicalName,
         CancellationToken ct = default
      )
      {
         var request = new RetrieveEntityRequest
         {
            LogicalName = logicalName,
            EntityFilters = EntityFilters.Entity
         };

         var metaResponse = (RetrieveEntityResponse)await provider
            .ExecuteAsync(request, ct);

         return metaResponse.EntityMetadata.PrimaryIdAttribute;
      }

      /// <summary>
      /// Gets the total record count for a specific entity type.
      /// </summary>
      /// <param name="provider">The Dataverse provider.</param>
      /// <param name="entityName">The logical name of the entity.</param>
      /// <param name="ct">A cancellation token.</param>
      /// <returns>The total number of records.</returns>
      public static async Task<long> GetRecordCountAsync(
         this IDataverseProvider provider,
         string entityName,
         CancellationToken ct = default
      )
      {
         var request = new Microsoft.Xrm.Sdk.Messages.RetrieveEntityRequest
         {
            LogicalName = entityName,
            EntityFilters = Microsoft.Xrm.Sdk.Metadata.EntityFilters.Entity
         };

         var metaResponse = (Microsoft.Xrm.Sdk.Messages.RetrieveEntityResponse)
            await provider.ExecuteAsync(request, ct);

         var primaryId = metaResponse.EntityMetadata.PrimaryIdAttribute ??
            $"{entityName}id";

         var fetchXml = $@"
            <fetch aggregate='true'>
              <entity name='{entityName}'>
                <attribute name='{primaryId}' alias='count' aggregate='count' />
              </entity>
            </fetch>";

         var result = await provider.RetrieveMultipleAsync(
            new Microsoft.Xrm.Sdk.Query.FetchExpression(fetchXml),
            ct
         );

         if (result.Entities.Count > 0 &&
             result.Entities[0].Contains("count"))
         {
            var entity = result.Entities[0];
            var aliasedValue = (Microsoft.Xrm.Sdk.AliasedValue)entity["count"];

            return Convert.ToInt64(aliasedValue.Value);
         }

         return 0;
      }
   }
}


// --- Source: dvmig.Core\Providers\LegacyCrmProvider.cs ---

namespace dvmig.Core.Providers
{
   /// <summary>
   /// Implementation of <see cref="IDataverseProvider"/> using the legacy
   /// <see cref="CrmServiceClient"/> from the XrmTooling.Connector SDK.
   /// Required for OnPrem (AD/IFD) authentication support.
   /// </summary>
   public class LegacyCrmProvider : IDataverseProvider, IDisposable
   {
      private readonly CrmServiceClient _client;
      private readonly AsyncRetryPolicy _retryPolicy;

      /// <inheritdoc />
      public string ConnectionString { get; }

      /// <summary>
      /// Initializes a new instance of the 
      /// <see cref="LegacyCrmProvider"/> class.
      /// </summary>
      /// <param name="connectionString">
      /// The connection string to the legacy CRM environment.
      /// </param>
      public LegacyCrmProvider(string connectionString)
      {
         ConnectionString = connectionString;
         _client = new CrmServiceClient(connectionString);

         if (!_client.IsReady)
            throw new Exception(
               $"Legacy CRM connection failed: {_client.LastCrmError}"
            );

         _retryPolicy = Policy
            .Handle<Exception>(EntityHelper.IsTransientError)
            .WaitAndRetryAsync(
               5,
               attempt => TimeSpan.FromSeconds(
                  Math.Min(Math.Pow(2, attempt + 2), 30)
               )
            );
      }

      private async Task<T> ExecuteWithRetryAsync<T>(
         Func<CancellationToken, Task<T>> action,
         CancellationToken ct
      )
      {
         return await _retryPolicy.ExecuteAsync(
            async () => await action(ct)
         );
      }

      private async Task ExecuteWithRetryAsync(
         Func<CancellationToken, Task> action,
         CancellationToken ct
      )
      {
         await _retryPolicy.ExecuteAsync(
            async () => await action(ct)
         );
      }

      /// <inheritdoc />
      public async Task<Entity?> RetrieveAsync(
         string entityLogicalName,
         Guid id,
         string[]? columns = null,
         CancellationToken ct = default
      )
      {
         return await ExecuteWithRetryAsync(
            async (token) => await Task.Run(() =>
            {
               try
               {
                  var columnSet = columns == null
                     ? new ColumnSet(true)
                     : new ColumnSet(columns);

                  return _client.Retrieve(
                     entityLogicalName,
                     id,
                     columnSet
                  );
               }
               catch (Exception ex)
               {
                  if (ex.IsNotFoundException())
                     return null;

                  throw;
               }
            }, token),
            ct
         );
      }

      /// <inheritdoc />
      public async Task<EntityMetadata?> GetEntityMetadataAsync(
         string entityLogicalName,
         CancellationToken ct = default
      )
      {
         return await ExecuteWithRetryAsync(
            async (token) => await Task.Run(() =>
            {
               var response = _client.Execute(
                  new RetrieveEntityRequest
                  {
                     LogicalName = entityLogicalName,
                     EntityFilters = EntityFilters.Attributes
                  }
               ) as RetrieveEntityResponse;

               return response?.EntityMetadata;
            }, token),
            ct
         );
      }

      /// <inheritdoc />
      public async Task<Guid> CreateAsync(
         Entity entity,
         CancellationToken ct = default,
         Guid? callerId = null
      )
      {
         return await ExecuteWithRetryAsync(
            async (token) => await Task.Run(() =>
            {
               if (callerId.HasValue && callerId.Value != Guid.Empty)
               {
                  clonedClient.CallerId = callerId.Value;

                  return clonedClient.Create(entity);
               }

               return _client.Create(entity);
            }, token),
            ct
         );
      }

      /// <inheritdoc />
      public async Task UpdateAsync(
         Entity entity,
         CancellationToken ct = default,
         Guid? callerId = null
      )
      {
         await ExecuteWithRetryAsync(
            async (token) => await Task.Run(() =>
            {
               if (callerId.HasValue && callerId.Value != Guid.Empty)
               {
                  clonedClient.CallerId = callerId.Value;
                  clonedClient.Update(entity);

                  return;
               }

               _client.Update(entity);
            }, token),
            ct
         );
      }

      /// <inheritdoc />
      public async Task DeleteAsync(
         string entityLogicalName,
         Guid id,
         CancellationToken ct = default
      )
      {
         await ExecuteWithRetryAsync(
            async (token) => await Task.Run(
               () => _client.Delete(entityLogicalName, id),
               token
            ),
            ct
         );
      }

      /// <inheritdoc />
      public async Task AssociateAsync(
         string entityLogicalName,
         Guid entityId,
         Relationship relationship,
         EntityReferenceCollection relatedEntities,
         CancellationToken ct = default
      )
      {
         await ExecuteWithRetryAsync(
            async (token) => await Task.Run(() => _client.Associate(
               entityLogicalName,
               entityId,
               relationship,
               relatedEntities
            ), token),
            ct
         );
      }

      /// <inheritdoc />
      public async Task<EntityCollection> RetrieveMultipleAsync(
         QueryBase query,
         CancellationToken ct = default
      )
      {
         return await ExecuteWithRetryAsync(
            async (token) => await Task.Run(
               () => _client.RetrieveMultiple(query),
               token
            ),
            ct
         );
      }

      /// <inheritdoc />
      public async Task<OrganizationResponse> ExecuteAsync(
         OrganizationRequest request,
         CancellationToken ct = default,
         Guid? callerId = null
      )
      {
         return await ExecuteWithRetryAsync(
            async (token) => await Task.Run(() =>
            {
               if (callerId.HasValue && callerId.Value != Guid.Empty)
               {
                  clonedClient.CallerId = callerId.Value;

                  return clonedClient.Execute(request);
               }

               return _client.Execute(request);
            }, token),
            ct
         );
      }

      /// <summary>
      /// Disposes the underlying CRM service client.
      /// </summary>
      public void Dispose()
      {
         _client.Dispose();
      }
   }
}


// --- Source: dvmig.Core\Provisioning\SchemaService.cs ---

namespace dvmig.Core.Provisioning
{
   /// <summary>
   /// Manages the creation of required schema components for migration.
   /// </summary>
   public class SchemaService : ISchemaService
   {
      private const int LanguageCode = 1033;
      private readonly ILogger _logger;

      private enum AttributeType
      {
         String,
         Memo,
         DateTime,
         Lookup
      }

      /// <summary>
      /// Initializes a new instance of the <see cref="SchemaService"/> class.
      /// </summary>
      /// <param name="logger">The logger instance.</param>
      public SchemaService(ILogger logger)
      {
         _logger = logger;
      }

      /// <inheritdoc />
      public async Task CreateSchemaAsync(
         IDataverseProvider target,
         CancellationToken ct = default
      )
      {
         // 1. dm_sourcedata
         await EnsureSourceDataEntityAsync(target, ct);

         // 2. dm_migrationfailure
         await EnsureFailureLogEntityAsync(target, ct);

         _logger.Information("Publishing changes...");

         await target.ExecuteAsync(new PublishAllXmlRequest(), ct);

         _logger.Information("Schema creation completed.");
      }

      private async Task EnsureSourceDataEntityAsync(
         IDataverseProvider target,
         CancellationToken ct
      )
      {
         var entityName = SystemConstants.SourceData.EntityLogicalName;
         var existingMeta = await target.GetEntityMetadataAsync(
            entityName,
            ct
         );

         if (existingMeta == null)
         {
            _logger.Information(
               "Creating '{Entity}' entity...",
               entityName
            );

            var entityReq = new CreateEntityRequest
            {
               Entity = new EntityMetadata
               {
                  SchemaName = entityName,
                  LogicalName = entityName,
                  DisplayName = new Label(
                     "DVMig Source Data",
                     LanguageCode
                  ),
                  DisplayCollectionName = new Label(
                     "DVMig Source Data",
                     LanguageCode
                  ),
                  OwnershipType = OwnershipTypes.UserOwned,
                  IsActivity = false,
                  HasNotes = false,
                  HasActivities = false
               },
               PrimaryAttribute = new StringAttributeMetadata
               {
                  SchemaName = SystemConstants.SourceData.Name,
                  LogicalName = SystemConstants.SourceData.Name,
                  DisplayName = new Label("Name", LanguageCode),
                  RequiredLevel =
                     new AttributeRequiredLevelManagedProperty(
                        AttributeRequiredLevel.None
                     ),
                  MaxLength = 100
               }
            };

            await target.ExecuteAsync(entityReq, ct);
            await Task.Delay(
               100,
               ct
            ); // Wait for propagation

            existingMeta = await target.GetEntityMetadataAsync(
               entityName,
               ct
            );
         }

         await CreateAttributeIfMissingAsync(
            target,
            entityName,
            existingMeta!,
            SystemConstants.SourceData.EntityId,
            "Source Entity ID",
            ct
         );

         await CreateAttributeIfMissingAsync(
            target,
            entityName,
            existingMeta!,
            SystemConstants.SourceData.EntityLogicalNameAttr,
            "Source Entity Logical Name",
            ct
         );

         await CreateAttributeIfMissingAsync(
            target,
            entityName,
            existingMeta!,
            SystemConstants.SourceData.CreatedOn,
            "Source Created Date",
            ct,
            AttributeType.DateTime
         );

         await CreateAttributeIfMissingAsync(
            target,
            entityName,
            existingMeta!,
            SystemConstants.SourceData.ModifiedOn,
            "Source Modified Date",
            ct,
            AttributeType.DateTime
         );
      }

      private async Task EnsureFailureLogEntityAsync(
         IDataverseProvider target,
         CancellationToken ct
      )
      {
         var entityName = SystemConstants.MigrationFailure.EntityLogicalName;
         var existingMeta = await target.GetEntityMetadataAsync(
            entityName,
            ct
         );

         if (existingMeta == null)
         {
            _logger.Information(
               "Creating '{Entity}' entity...",
               entityName
            );

            var entityReq = new CreateEntityRequest
            {
               Entity = new EntityMetadata
               {
                  SchemaName = entityName,
                  LogicalName = entityName,
                  DisplayName = new Label("DVMig Failure", LanguageCode),
                  DisplayCollectionName = new Label(
                     "DVMig Failures",
                     LanguageCode
                  ),
                  OwnershipType = OwnershipTypes.UserOwned,
                  IsActivity = false
               },
               PrimaryAttribute = new StringAttributeMetadata
               {
                  SchemaName = SystemConstants.MigrationFailure.Name,
                  LogicalName = SystemConstants.MigrationFailure.Name,
                  DisplayName = new Label("Name", LanguageCode),
                  MaxLength = 100
               }
            };

            await target.ExecuteAsync(entityReq, ct);
            await Task.Delay(
               100,
               ct
            );

            existingMeta = await target.GetEntityMetadataAsync(
               entityName,
               ct
            );
         }

         await CreateAttributeIfMissingAsync(
            target,
            entityName,
            existingMeta!,
            SystemConstants.MigrationFailure.SourceId,
            "Source Record ID",
            ct
         );

         await CreateAttributeIfMissingAsync(
            target,
            entityName,
            existingMeta!,
            SystemConstants.MigrationFailure.EntityLogicalNameAttr,
            "Entity Logical Name",
            ct
         );

         await CreateAttributeIfMissingAsync(
            target,
            entityName,
            existingMeta!,
            SystemConstants.MigrationFailure.ErrorMessage,
            "Error Message",
            ct,
            AttributeType.Memo
         );

         await CreateAttributeIfMissingAsync(
            target,
            entityName,
            existingMeta!,
            SystemConstants.MigrationFailure.Timestamp,
            "Failure Timestamp",
            ct,
            AttributeType.DateTime
         );
      }

      private async Task CreateAttributeIfMissingAsync(
         IDataverseProvider target,
         string entityLogicalName,
         EntityMetadata entityMeta,
         string schemaName,
         string displayName,
         CancellationToken ct,
         AttributeType type = AttributeType.String,
         string? lookupTarget = null
      )
      {
         if (entityMeta.Attributes != null &&
             entityMeta.Attributes.Any(a => a.LogicalName == schemaName))
            return;

         _logger.Information(
            "Creating attribute {Attr} on {Entity}...",
            schemaName,
            entityLogicalName
         );

         AttributeMetadata attr = type switch
         {
            AttributeType.Memo => new MemoAttributeMetadata
            {
               SchemaName = schemaName,
               LogicalName = schemaName.ToLower(),
               DisplayName = new Label(displayName, LanguageCode),
               MaxLength = SystemConstants.AppConstants
                                .MaxMemoFieldLength
            },
            AttributeType.DateTime => new DateTimeAttributeMetadata
            {
               SchemaName = schemaName,
               LogicalName = schemaName.ToLower(),
               DisplayName = new Label(displayName, LanguageCode),
               Format = DateTimeFormat.DateAndTime
            },
            AttributeType.Lookup => new LookupAttributeMetadata
            {
               SchemaName = schemaName,
               LogicalName = schemaName.ToLower(),
               DisplayName = new Label(displayName, LanguageCode),
               Targets = new[] { lookupTarget! }
            },
            _ => new StringAttributeMetadata
            {
               SchemaName = schemaName,
               LogicalName = schemaName.ToLower(),
               DisplayName = new Label(displayName, LanguageCode),
               MaxLength = 200
            }
         };

         var req = new CreateAttributeRequest
         {
            EntityName = entityLogicalName,
            Attribute = attr
         };

         await target.ExecuteAsync(req, ct);
         await Task.Delay(10, ct); // Gap for consistency
      }

      /// <inheritdoc />
      public async Task DropSchemaAsync(
         IDataverseProvider target,
         CancellationToken ct = default
      )
      {
         // 1. dm_migrationfailure (Delete this first as it might reference 
         //    dm_sourcedata if lookup was manually added)
         await DropEntityIfPresentAsync(
            target,
            SystemConstants.MigrationFailure.EntityLogicalName,
            ct
         );

         _logger.Information("Publishing changes...");
         await target.ExecuteAsync(new PublishAllXmlRequest(), ct);

         // 2. dm_sourcedata
         await DropEntityIfPresentAsync(
            target,
            SystemConstants.SourceData.EntityLogicalName,
            ct
         );

         _logger.Information("Publishing changes...");
         await target.ExecuteAsync(new PublishAllXmlRequest(), ct);

         _logger.Information("Schema removal completed.");
      }

      private async Task DropEntityIfPresentAsync(
         IDataverseProvider target,
         string logicalName,
         CancellationToken ct
      )
      {
         _logger.Information(
            "Checking for '{Entity}' entity...",
            logicalName
         );

         var existingMeta = await target.GetEntityMetadataAsync(
            logicalName,
            ct
         );

         if (existingMeta != null)
         {
            _logger.Information(
               "Deleting '{Entity}' entity...",
               logicalName
            );

            try
            {
               var request = new DeleteEntityRequest
               {
                  LogicalName = logicalName
               };

               await target.ExecuteAsync(request, ct);
            }
            catch (FaultException ex) when (
               ex.Message.Contains("referenced by")
            )
            {
               _logger.Warning(
                  "Deletion of {Entity} failed due to dependencies.",
                  logicalName
               );

               var depReq = new RetrieveDependenciesForDeleteRequest
               {
                  ComponentType = 1, // Entity
                  ObjectId = existingMeta.MetadataId ?? Guid.Empty
               };

               var depRes = await target.ExecuteAsync(depReq, ct)
                  as RetrieveDependenciesForDeleteResponse;

               var blockers = new List<string>();

               if (depRes?.EntityCollection.Entities.Any() == true)
               {
                  foreach (var dep in depRes.EntityCollection.Entities)
                  {
                     var depType = dep.GetAttributeValue<OptionSetValue>(
                        "dependentcomponenttype")?.Value;
                     var depId = dep.GetAttributeValue<Guid>(
                        "dependentcomponentobjectid");

                     string? depName = await TryGetDependencyNameAsync(
                        target,
                        depType ?? 0,
                        depId,
                        ct
                     );

                     if (!string.IsNullOrEmpty(depName))
                        blockers.Add($"{depName} (Type {depType})");
                     else
                        blockers.Add(
                           $"Unknown Component {depId} (Type {depType})"
                        );
                  }
               }

               var blockerList = blockers.Count > 0
                  ? string.Join(", ", blockers)
                  : "unidentified components";

               var errorMsg =
                  $"Cannot delete entity '{logicalName}' because it is " +
                  $"referenced by: {blockerList}. Please manually remove " +
                  "these references (e.g., from Model-driven Apps, " +
                  "Sitemaps, or Solutions) before trying again.";

               _logger.Error(errorMsg);

               throw new InvalidOperationException(errorMsg, ex);
            }
         }
         else
            _logger.Information(
               "'{Entity}' entity not found.",
               logicalName
            );
      }

      private async Task<string?> TryGetDependencyNameAsync(
         IDataverseProvider target,
         int type,
         Guid id,
         CancellationToken ct
      )
      {
         try
         {
            string? entityName = type switch
            {
               62 => SystemConstants.PluginRegistration.StepEntity,
               80 => "appmodule",
               29 => "workflow",
               60 => "systemform",
               24 => "systemform",
               _ => null
            };

            if (entityName == null)
               return null;

            var result = await target.RetrieveAsync(
               entityName,
               id,
               new[] { SystemConstants.DataverseAttributes.Name },
               ct
            );

            return result?.GetAttributeValue<string>(
               SystemConstants.DataverseAttributes.Name
            );
         }
         catch
         {
            return null;
         }
      }
   }
}


// --- Source: dvmig.Core\Provisioning\SeedingService.cs ---

namespace dvmig.Core.Provisioning
{
   /// <summary>
   /// Implementation of <see cref="ISeedingService"/> that seeds and cleans 
   /// test data in Dataverse.
   /// </summary>
   public class SeedingService : ISeedingService
   {
      private readonly ILogger _logger;
      private readonly IUserService _userService;

      /// <summary>
      /// Initializes a new instance of the <see cref="SeedingService"/> class.
      /// </summary>
      /// <param name="logger">The logger instance.</param>
      /// <param name="userService">The user service instance.</param>
      public SeedingService(ILogger logger, IUserService userService)
      {
         _logger = logger;
         _userService = userService;
      }

      /// <inheritdoc />
      public async Task SeedSampleDataAsync(
         IDataverseProvider provider,
         int recordCount = 50,
         CancellationToken ct = default
      )
      {
         _logger.Information(
            $"Seeding {recordCount} Accounts with related data..."
         );

         var faker = new Faker();
         var availableUserIds = await _userService.GetRealActiveUsersAsync(
            provider,
            ct
         );

         var activityTypes = DataverseEntities.ToList()
            .Where(e => e.IsActivityEntity && !e.IsSystemEntity)
            .Select(e => e.Name)
            .ToArray();

         for (int i = 0; i < recordCount; i++)
         {
            var (accountId, createdDate, accountCallerId) =
               await CreateAccountAsync(provider, faker, availableUserIds, ct);

            var contactIds = await CreateContactsForAccountAsync(
               provider,
               faker,
               accountId,
               createdDate,
               availableUserIds,
               ct
            );

            await SetPrimaryContactAsync(
               provider,
               faker,
               accountId,
               contactIds,
               accountCallerId,
               ct
            );

            var activityCount = await CreateActivitiesForAccountAsync(
               provider,
               faker,
               accountId,
               contactIds,
               activityTypes,
               createdDate,
               availableUserIds,
               ct
            );

            _logger.Information(
               $"Account {i + 1}/{recordCount} seeded with " +
               $"{contactIds.Count} contacts and {activityCount} activities."
            );
         }

         _logger.Information("Seeding complete.");
      }

      private async Task<(Guid Id, DateTime CreatedOn, Guid? CallerId)>
         CreateAccountAsync(
            IDataverseProvider provider,
            Faker faker,
            List<Guid> availableUserIds,
            CancellationToken ct
         )
      {
         var account = new Entity(DataverseEntities.Account.Name);
         account[DataverseAttributes.Name] = faker.Company.CompanyName();
         account[DataverseAttributes.Telephone1] = faker.Phone.PhoneNumber();

         var createdDate = faker.Date.Past(2);
         var modifiedDate = faker.Date.Between(createdDate, DateTime.UtcNow);

         account[DataverseAttributes.OverriddenCreatedOn] = createdDate;
         account[DataverseAttributes.ModifiedOn] = modifiedDate;

         var callerId = availableUserIds.Count > 0
            ? faker.PickRandom(availableUserIds)
            : (Guid?)null;

         if (callerId.HasValue)
         {
            account[DataverseAttributes.CreatedBy] = new EntityReference(
               DataverseEntities.SystemUser.Name,
               callerId.Value
            );
         }

         var accountId = await provider.CreateAsync(account, ct, callerId);

         return (accountId, createdDate, callerId);
      }

      private async Task<List<Guid>> CreateContactsForAccountAsync(
         IDataverseProvider provider,
         Faker faker,
         Guid accountId,
         DateTime accountCreatedDate,
         List<Guid> availableUserIds,
         CancellationToken ct
      )
      {
         var contactIds = new List<Guid>();
         int contactCount = faker.Random.Int(2, 7);

         for (int j = 0; j < contactCount; j++)
         {
            var contact = new Entity(DataverseEntities.Contact.Name);
            contact[DataverseAttributes.FirstName] = faker.Name.FirstName();
            contact[DataverseAttributes.LastName] = faker.Name.LastName();
            contact[DataverseAttributes.EmailAddress1] = faker.Internet.Email();

            contact[DataverseAttributes.ParentCustomerId] =
               new EntityReference(DataverseEntities.Account.Name, accountId);

            var createdDate = faker.Date.Between(
               accountCreatedDate,
               DateTime.UtcNow
            );

            var modifiedDate = faker.Date.Between(createdDate, DateTime.UtcNow);

            contact[DataverseAttributes.OverriddenCreatedOn] = createdDate;
            contact[DataverseAttributes.ModifiedOn] = modifiedDate;

            var callerId = availableUserIds.Count > 0
               ? faker.PickRandom(availableUserIds)
               : (Guid?)null;

            if (callerId.HasValue)
            {
               contact[DataverseAttributes.CreatedBy] = new EntityReference(
                  DataverseEntities.SystemUser.Name,
                  callerId.Value
               );
            }

            var contactId = await provider.CreateAsync(contact, ct, callerId);
            contactIds.Add(contactId);
         }

         return contactIds;
      }

      private async Task SetPrimaryContactAsync(
         IDataverseProvider provider,
         Faker faker,
         Guid accountId,
         List<Guid> contactIds,
         Guid? callerId,
         CancellationToken ct
      )
      {
         var primaryContactId = faker.PickRandom(contactIds);
         var accountUpdate = new Entity(
            DataverseEntities.Account.Name,
            accountId
         );

         accountUpdate[DataverseAttributes.PrimaryContactId] =
            new EntityReference(DataverseEntities.Contact.Name, primaryContactId);

         accountUpdate[DataverseAttributes.ModifiedOn] = DateTime.UtcNow;

         await provider.UpdateAsync(accountUpdate, ct, callerId);
      }

      private async Task<int> CreateActivitiesForAccountAsync(
         IDataverseProvider provider,
         Faker faker,
         Guid accountId,
         List<Guid> contactIds,
         string[] activityTypes,
         DateTime accountCreatedDate,
         List<Guid> availableUserIds,
         CancellationToken ct
      )
      {
         int activityCount = faker.Random.Int(5, 12);

         for (int k = 0; k < activityCount; k++)
         {
            var logicalName = faker.PickRandom(activityTypes);
            var activity = new Entity(logicalName);

            activity[DataverseAttributes.Subject] = faker.Lorem.Sentence(5);
            activity[DataverseAttributes.Description] = faker.Lorem.Paragraph();
            activity[DataverseAttributes.ScheduledEnd] = faker.Date.Future();

            var createdDate = faker.Date.Between(
               accountCreatedDate,
               DateTime.UtcNow
            );

            var modifiedDate = faker.Date.Between(createdDate, DateTime.UtcNow);

            activity[DataverseAttributes.OverriddenCreatedOn] = createdDate;
            activity[DataverseAttributes.ModifiedOn] = modifiedDate;

            if (faker.Random.Bool())
            {
               activity[DataverseAttributes.RegardingObjectId] =
                  new EntityReference(DataverseEntities.Account.Name, accountId);
            }
            else
            {
               activity[DataverseAttributes.RegardingObjectId] =
                  new EntityReference(
                     DataverseEntities.Contact.Name,
                     faker.PickRandom(contactIds)
                  );
            }

            var callerId = availableUserIds.Count > 0
               ? faker.PickRandom(availableUserIds)
               : (Guid?)null;

            if (callerId.HasValue)
            {
               activity[DataverseAttributes.CreatedBy] = new EntityReference(
                  DataverseEntities.SystemUser.Name,
                  callerId.Value
               );
            }

            await provider.CreateAsync(activity, ct, callerId);
         }

         return activityCount;
      }
   }
}


// --- Source: dvmig.Core\Provisioning\ValidationService.cs ---

namespace dvmig.Core.Provisioning
{
   /// <summary>
   /// Implementation of <see cref="IValidationService"/> that validates 
   /// environment readiness.
   /// </summary>
   public class ValidationService : IValidationService
   {
      /// <inheritdoc />
      public async Task<bool> ValidateTargetEnvironmentAsync(
         IDataverseProvider target,
         CancellationToken ct = default
      )
      {
         try
         {
            // 1. Check Failure Log Entity
            var failureMeta = await target.GetEntityMetadataAsync(
               SystemConstants.MigrationFailure.EntityLogicalName,
               ct
            );

            if (failureMeta == null)
               return false;

            // 2. Check Source Data Entity
            var sourceDataMeta = await target.GetEntityMetadataAsync(
               SystemConstants.SourceData.EntityLogicalName,
               ct
            );

            if (sourceDataMeta == null)
               return false;

            // 3. Check Plugin Assembly
            var assemblyQuery = new QueryByAttribute(
               SystemConstants.PluginRegistration.AssemblyEntity
            )
            {
               ColumnSet = new ColumnSet(
                  SystemConstants.PluginRegistration.AssemblyId
               )
            };

            assemblyQuery.AddAttributeValue(
               SystemConstants.PluginRegistration.AssemblyName,
               SystemConstants.AppConstants.PluginName
            );

            var assemblies = await target.RetrieveMultipleAsync(
               assemblyQuery,
               ct
            );

            if (!assemblies.Entities.Any())
               return false;

            // 4. Check Plugin Type
            var typeQuery = new QueryByAttribute(
               SystemConstants.PluginRegistration.TypeEntity
            )
            {
               ColumnSet = new ColumnSet(
                  SystemConstants.PluginRegistration.TypeId
               )
            };

            typeQuery.AddAttributeValue(
               SystemConstants.PluginRegistration.TypeName,
               $"{SystemConstants.AppConstants.PluginName}.DMPlugin"
            );

            var types = await target.RetrieveMultipleAsync(typeQuery, ct);

            if (!types.Entities.Any())
               return false;

            var typeId = types.Entities.First().Id;

            // 5. Check Plugin Steps (Create & Update)
            var stepQuery = new QueryByAttribute(
               SystemConstants.PluginRegistration.StepEntity
            )
            {
               ColumnSet = new ColumnSet(
                  SystemConstants.PluginRegistration.MessageName
               )
            };

            stepQuery.AddAttributeValue(
               SystemConstants.PluginRegistration.EventHandler,
               typeId
            );

            var steps = await target.RetrieveMultipleAsync(stepQuery, ct);

            bool hasCreate = steps.Entities.Any(e =>
               e.GetAttributeValue<string>(
                  SystemConstants.PluginRegistration.MessageName
               )?.Contains("Create") == true
            );

            bool hasUpdate = steps.Entities.Any(e =>
               e.GetAttributeValue<string>(
                  SystemConstants.PluginRegistration.MessageName
               )?.Contains("Update") == true
            );

            return hasCreate && hasUpdate;
         }
         catch
         {
            return false;
         }
      }
   }
}


// --- Source: dvmig.Core\Provisioning\WipeDataService.cs ---

namespace dvmig.Core.Provisioning
{
   /// <summary>
   /// Implementation of <see cref="IWipeDataService"/> that handles 
   /// efficient, parallel data removal from Dataverse environments.
   /// </summary>
   public class WipeDataService : IWipeDataService
   {
      private readonly ILogger _logger;

      /// <summary>
      /// Initializes a new instance of the
      /// <see cref="WipeDataService"/> class.
      /// </summary>
      /// <param name="logger">The logger instance.</param>
      public WipeDataService(ILogger logger)
      {
         _logger = logger;
      }

      /// <inheritdoc />
      public async Task WipeEntitiesAsync(
         IDataverseProvider provider,
         List<string>? entities = null,
         IProgress<long>? progress = null,
         CancellationToken ct = default
      )
      {
         var targetEntities = entities ??
            SystemConstants.SyncSettings.RecommendedEntities.ToList();

         _logger.Information("Pass 1/2: Disassociating records...");

         foreach (var logicalName in targetEntities)
            await DisassociateEntityRecordsAsync(provider, logicalName, ct);

         _logger.Information("Pass 2/2: Deleting records...");

         // Reverse to handle potential simple dependencies 
         // (e.g., delete contacts before accounts if needed)
         targetEntities.Reverse();

         long totalDeleted = 0;
         long initialTotal = 0;

         foreach (var entity in targetEntities)
            initialTotal += await provider.GetRecordCountAsync(entity, ct);

         progress?.Report(initialTotal);

         foreach (var logicalName in targetEntities)
         {
            totalDeleted += await WipeEntityRecordsAsync(
               provider,
               logicalName,
               initialTotal,
               totalDeleted,
               progress,
               ct
            );
         }

         _logger.Information("Cleanup complete.");
      }

      private async Task DisassociateEntityRecordsAsync(
         IDataverseProvider provider,
         string logicalName,
         CancellationToken ct
      )
      {
         var nullableLookups = await GetNullableLookupAttributesAsync(
            provider,
            logicalName,
            ct
         );

         if (nullableLookups.Count == 0)
            return;

         _logger.Information($"Disassociating {logicalName}...");

         while (true)
         {
            var query = new QueryExpression(logicalName)
            {
               ColumnSet = new ColumnSet(nullableLookups.ToArray()),
               TopCount = 1000
            };

            var filter = new FilterExpression(LogicalOperator.Or);

            foreach (var attr in nullableLookups)
               filter.AddCondition(attr, ConditionOperator.NotNull);

            query.Criteria = filter;

            var results = await provider.RetrieveMultipleAsync(query, ct);

            if (results.Entities.Count == 0)
               break;

            var chunks = results.Entities
               .Select((e, i) => new { Entity = e, Index = i })
               .GroupBy(x => x.Index / 100)
               .Select(g => g.Select(x => x.Entity).ToList())
               .ToList();

            var parallelOptions = new ParallelOptions
            {
               MaxDegreeOfParallelism = 10,
               CancellationToken = ct
            };

            using (
               var semaphore = new SemaphoreSlim(
                  parallelOptions.MaxDegreeOfParallelism
               )
            )
            {
               var tasks = chunks.Select(async chunk =>
               {
                  await semaphore.WaitAsync(ct);

                  try
                  {
                     var multipleRequest = new ExecuteMultipleRequest
                     {
                        Settings = new ExecuteMultipleSettings
                        {
                           ContinueOnError = true,
                           ReturnResponses = false
                        },
                        Requests = new OrganizationRequestCollection()
                     };

                     foreach (var entity in chunk)
                     {
                        var updateEntity = new Entity(logicalName, entity.Id);

                        foreach (var attr in nullableLookups)
                           if (entity.Contains(attr))
                              updateEntity[attr] = null;

                        multipleRequest.Requests.Add(
                           new UpdateRequest { Target = updateEntity }
                        );
                     }

                     await provider.ExecuteAsync(multipleRequest, ct);
                  }
                  finally
                  {
                     semaphore.Release();
                  }
               });

               await Task.WhenAll(tasks);
            }
         }
      }

      private async Task<List<string>> GetNullableLookupAttributesAsync(
         IDataverseProvider provider,
         string logicalName,
         CancellationToken ct
      )
      {
         var metadata = await provider.GetEntityMetadataAsync(logicalName, ct);

         if (metadata == null || metadata.Attributes == null)
            return new List<string>();

         return metadata.Attributes
            .Where(a =>
               (a.AttributeType == AttributeTypeCode.Lookup ||
                a.AttributeType == AttributeTypeCode.Customer) &&
               a.IsValidForUpdate == true &&
               a.RequiredLevel?.Value == AttributeRequiredLevel.None
            )
            .Select(a => a.LogicalName)
            .ToList();
      }

      private async Task<long> WipeEntityRecordsAsync(
         IDataverseProvider provider,
         string logicalName,
         long initialTotal,
         long alreadyDeletedTotal,
         IProgress<long>? progress,
         CancellationToken ct
      )
      {
         _logger.Information($"Cleaning {logicalName}...");

         var lockObject = new object();
         var deletedForThisEntity = 0L;
         var consecutiveFailures = 0;
         const int MaxConsecutiveFailures = 3;

         while (consecutiveFailures < MaxConsecutiveFailures)
         {
            var query = new QueryExpression(logicalName)
            {
               ColumnSet = new ColumnSet(false),
               TopCount = 1000
            };

            var results = await provider.RetrieveMultipleAsync(query, ct);

            if (results.Entities.Count == 0)
               break;

            var chunks = results.Entities
               .Select((e, i) => new { Entity = e, Index = i })
               .GroupBy(x => x.Index / 100)
               .Select(g => g.Select(x => x.Entity).ToList())
               .ToList();

            var parallelOptions = new ParallelOptions
            {
               MaxDegreeOfParallelism = 10,
               CancellationToken = ct
            };

            var deletedInThisIteration = 0L;

            using (
               var semaphore = new SemaphoreSlim(
                  parallelOptions.MaxDegreeOfParallelism
               )
            )
            {
               var tasks = chunks.Select(async chunk =>
               {
                  await semaphore.WaitAsync(ct);

                  try
                  {
                     var successfulCount =
                        await DeleteRecordBatchWithCountAsync(
                           provider,
                           chunk,
                           ct
                        );

                     lock (lockObject)
                     {
                        deletedForThisEntity += successfulCount;
                        deletedInThisIteration += successfulCount;

                        progress?.Report(
                           Math.Max(
                              0,
                              initialTotal -
                                 (alreadyDeletedTotal + deletedForThisEntity)
                           )
                        );
                     }
                  }
                  finally
                  {
                     semaphore.Release();
                  }
               });

               await Task.WhenAll(tasks);
            }

            // If we didn't manage to delete anything in this whole iteration 
            // of 1000 records, something is wrong (likely constraints).
            if (deletedInThisIteration == 0)
            {
               consecutiveFailures++;
               
               if (consecutiveFailures < MaxConsecutiveFailures)
                  _logger.Warning(
                     $"Could not delete any records for {logicalName} " +
                     "in this batch. It might be due to dependencies. " +
                     $"Retry {consecutiveFailures}/{MaxConsecutiveFailures}..."
                  );
            }
            else
               consecutiveFailures = 0; // Reset on success
         }

         if (consecutiveFailures >= MaxConsecutiveFailures)
            _logger.Error(
               $"Skipping remaining records for {logicalName} after " +
               $"{MaxConsecutiveFailures} failed attempts. " +
               "This is usually caused by circular dependencies or " +
               "mandatory relationships."
            );

         return deletedForThisEntity;
      }

      private async Task<long> DeleteRecordBatchWithCountAsync(
         IDataverseProvider provider,
         List<Entity> chunk,
         CancellationToken ct
      )
      {
         var multipleRequest = new ExecuteMultipleRequest
         {
            Settings = new ExecuteMultipleSettings
            {
               ContinueOnError = true,
               ReturnResponses = true // We need responses to count successes
            },
            Requests = new OrganizationRequestCollection()
         };

         foreach (var entity in chunk)
            multipleRequest.Requests.Add(new DeleteRequest
            {
               Target = entity.ToEntityReference()
            });

         var response = (ExecuteMultipleResponse)await provider
            .ExecuteAsync(multipleRequest, ct);

         // Count records that didn't return an error
         
         return response.Responses.Count(r => r.Fault == null);
      }
   }
}


// --- Source: dvmig.Core\Settings\SettingsService.cs ---

namespace dvmig.Core.Settings
{
   /// <summary>
   /// Implementation of the settings service using local file storage and 
   /// DPAPI for encryption (on Windows).
   /// </summary>
   public class SettingsService : ISettingsService
   {
      private readonly string _filePath;

      private static readonly byte[] LegacyEntropy =
         Encoding.UTF8.GetBytes("dvmig-entropy");

      /// <summary>
      /// Initializes a new instance of the 
      /// <see cref="SettingsService"/> class.
      /// </summary>
      public SettingsService()
      {
         string folder;

         bool isWindows =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

         if (isWindows)
         {
            folder = Path.Combine(
               Environment.GetFolderPath(
                  Environment.SpecialFolder.ApplicationData
               ),
               SystemConstants.AppConstants.AppName
            );
         }
         else
         {
            // Use hidden folder in home directory for Linux/macOS
            folder = Path.Combine(
               Environment.GetFolderPath(
                  Environment.SpecialFolder.UserProfile
               ),
               "." + SystemConstants.AppConstants.AppName
            );
         }

         if (!Directory.Exists(folder))
            Directory.CreateDirectory(folder);

         _filePath = Path.Combine(
            folder,
            SystemConstants.AppConstants.SettingsFileName
         );
      }

      /// <inheritdoc />
      public UserSettings LoadSettings()
      {
         if (!File.Exists(_filePath))
            return new UserSettings();

         try
         {
            var json = File.ReadAllText(_filePath);
            var settings = JsonSerializer.Deserialize<UserSettings>(json) ??
               new UserSettings();

            if (settings.RememberConnections)
            {
               settings.SourceConnectionString =
                  Decrypt(settings.SourceConnectionString);

               settings.TargetConnectionString =
                  Decrypt(settings.TargetConnectionString);
            }
            else
            {
               settings.SourceConnectionString = string.Empty;
               settings.TargetConnectionString = string.Empty;
            }

            return settings;
         }
         catch
         {
            return new UserSettings();
         }
      }

      /// <inheritdoc />
      public void SaveSettings(UserSettings settings)
      {
         try
         {
            var settingsCopy = new UserSettings
            {
               RememberConnections = settings.RememberConnections,
               AutoConnect = settings.AutoConnect,
               Language = settings.Language,
               MaxParallelism = settings.MaxParallelism
            };

            if (settings.RememberConnections)
            {
               settingsCopy.SourceConnectionString =
                  Encrypt(settings.SourceConnectionString);

               settingsCopy.TargetConnectionString =
                  Encrypt(settings.TargetConnectionString);
            }

            var json = JsonSerializer.Serialize(settingsCopy);

            File.WriteAllText(_filePath, json);
         }
         catch
         {
            // Log error in real app
         }
      }

      /// <summary>
      /// Encrypts the specified text.
      /// </summary>
      private string Encrypt(string text)
      {
         if (string.IsNullOrEmpty(text))
            return string.Empty;

         bool isWindows =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

         // DPAPI is only available on Windows
         if (!isWindows)
            return text;

         try
         {
            var data = Encoding.UTF8.GetBytes(text);
            var encrypted = ProtectedData.Protect(
               data,
               LegacyEntropy,
               DataProtectionScope.CurrentUser
            );

            return Convert.ToBase64String(encrypted);
         }
         catch
         {
            return text;
         }
      }

      /// <summary>
      /// Decrypts the specified base64 string.
      /// </summary>
      private string Decrypt(string base64)
      {
         if (string.IsNullOrEmpty(base64))
            return string.Empty;

         bool isWindows =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

         // DPAPI is only available on Windows
         if (!isWindows)
            return base64;

         try
         {
            var bytes = Convert.FromBase64String(base64);

            try
            {
               // Try mandated decryption with legacy entropy
               var decrypted = ProtectedData.Unprotect(
                  bytes,
                  LegacyEntropy,
                  DataProtectionScope.CurrentUser
               );

               return Encoding.UTF8.GetString(decrypted);
            }
            catch(CryptographicException)
            {
               // Fallback: try Jules' "no-entropy" format just in case
               var decrypted = ProtectedData.Unprotect(
                  bytes,
                  null,
                  DataProtectionScope.CurrentUser
               );

               return Encoding.UTF8.GetString(decrypted);
            }
         }
         catch
         {
            return base64;
         }
      }
   }
}



// --- Source: dvmig.Core\Settings\StringMasker.cs ---


namespace dvmig.Core.Settings
{
   /// <summary>
   /// Provides helper methods for manipulating Dataverse connection strings.
   /// </summary>
   public static class StringMasker
   {
      /// <summary>
      /// Masks sensitive information (passwords, secrets) in a Dataverse 
      /// connection string for secure display.
      /// </summary>
      /// <param name="connectionString">
      /// The raw connection string to mask.
      /// </param>
      /// <returns>A masked version of the connection string.</returns>
      public static string MaskConnectionString(string connectionString)
      {
         if (string.IsNullOrEmpty(connectionString))
            return string.Empty;

         var parts = connectionString.Split(
            new[] { ';' },
            StringSplitOptions.RemoveEmptyEntries
         );

         var maskedParts = new List<string>();

         foreach (var part in parts)
         {
            var kv = part.Split(new[] { '=' }, 2);

            if (kv.Length != 2)
            {
               maskedParts.Add(part);

               continue;
            }

            var key = kv[0].Trim();
            var val = kv[1].Trim();

            if (IsSensitive(key))
               maskedParts.Add($"{key}=********");
            else
               maskedParts.Add($"{key}={val}");
         }

         return string.Join(";", maskedParts);
      }

      /// <summary>
      /// Extracts only the environment URL from a connection string or 
      /// returns the string itself if it's already a URL.
      /// </summary>
      public static string GetEnvironmentUrl(string connectionString)
      {
         if (string.IsNullOrWhiteSpace(connectionString))
            return "(Not Set)".t();

         var comp = StringComparison.OrdinalIgnoreCase;

         // Check if it's already a raw URL
         if ((connectionString.StartsWith(Connection.Https, comp) ||
              connectionString.StartsWith(Connection.Http, comp)) &&
             !connectionString.Contains(";"))
         {
            var url = connectionString;

            if (url.StartsWith(Connection.Https, comp))
               url = url.Substring(8);
            else if (url.StartsWith(Connection.Http, comp))
               url = url.Substring(7);

            return url.TrimEnd('/');
         }

         var parts = connectionString.Split(
            new[] { ';' },
            StringSplitOptions.RemoveEmptyEntries
         );

         foreach (var part in parts)
         {
            var kv = part.Split(new[] { '=' }, 2);

            if (kv.Length != 2)
               continue;

            var key = kv[0].Trim();
            var val = kv[1].Trim();

            if (string.Equals(key, Connection.Url, comp) ||
                string.Equals(key, Connection.ServiceUri, comp) ||
                string.Equals(key, Connection.Server, comp))
            {
               var url = val;

               if (url.StartsWith(Connection.Https, comp))
                  url = url.Substring(8);
               else if (url.StartsWith(Connection.Http, comp))
                  url = url.Substring(7);

               return url.TrimEnd('/');
            }
         }

         return connectionString.Length > 40
            ? connectionString.Substring(0, 37) + "..."
            : connectionString;
      }

      private static bool IsSensitive(string key)
      {
         var comp = StringComparison.OrdinalIgnoreCase;

         return 
            key.IndexOf(MaskingKeywords.Password, comp) >= 0 ||
            key.IndexOf(MaskingKeywords.Secret, comp) >= 0 ||
            key.IndexOf(MaskingKeywords.Token, comp) >= 0 ||
            key.IndexOf(MaskingKeywords.Thumbprint, comp) >= 0;
      }
   }
}


// --- Source: dvmig.Core\Settings\UserSettings.cs ---
namespace dvmig.Core.Settings
{
   /// <summary>
   /// Represents the persisted user settings for the application.
   /// </summary>
   public class UserSettings
   {
      /// <summary>
      /// Gets or sets the encrypted connection string for the source 
      /// environment.
      /// </summary>
      public string SourceConnectionString { get; set; } = string.Empty;

      /// <summary>
      /// Gets or sets the encrypted connection string for the target 
      /// environment.
      /// </summary>
      public string TargetConnectionString { get; set; } = string.Empty;

      /// <summary>
      /// Gets or sets a value indicating whether connection strings 
      /// should be persisted.
      /// </summary>
      public bool RememberConnections { get; set; }

      /// <summary>
      /// Gets or sets a value indicating whether to automatically attempt 
      /// connection on startup.
      /// </summary>
      public bool AutoConnect { get; set; }

      /// <summary>
      /// Gets or sets the preferred language code (e.g., "en", "sv").
      /// </summary>
      public string Language { get; set; } = "en";

      /// <summary>
      /// Gets or sets the maximum number of parallel sync threads.
      /// </summary>
      public int MaxParallelism { get; set; } = 10;
   }
}


// --- Source: dvmig.Core\Shared\EntityHelper.cs ---


namespace dvmig.Core.Shared
{
   /// <summary>
   /// Provides utility methods for working with Dataverse entities.
   /// </summary>
   public static class EntityHelper
   {
      /// <summary>
      /// Generates a standardized record key for tracking and caching.
      /// </summary>
      /// <param name="logicalName">The logical name of the entity.</param>
      /// <param name="id">The unique identifier of the record.</param>
      /// <returns>A formatted string key.</returns>
      public static string GetRecordKey(string logicalName, System.Guid id)
      {
         return $"{logicalName.ToLowerInvariant()}:{id}";
      }

      /// <summary>
      /// Generates a standardized record key for an entity.
      /// </summary>
      /// <param name="entity">The entity record.</param>
      /// <returns>A formatted string key.</returns>
      public static string GetRecordKey(Entity entity)
      {
         return GetRecordKey(entity.LogicalName, entity.Id);
      }

      /// <summary>
      /// Generates a standardized record key for an entity reference.
      /// </summary>
      /// <param name="er">The entity reference.</param>
      /// <returns>A formatted string key.</returns>
      public static string GetRecordKey(EntityReference er)
      {
         return GetRecordKey(er.LogicalName, er.Id);
      }

      /// <summary>
      /// Creates a shallow clone of an entity, copying its attributes.
      /// </summary>
      /// <param name="entity">The entity to clone.</param>
      /// <returns>A new entity instance with the same ID and attributes.</returns>
      public static Entity Clone(Entity entity)
      {
         var clone = new Entity(entity.LogicalName, entity.Id);

         foreach (var attr in entity.Attributes)
            clone[attr.Key] = attr.Value;

         return clone;
      }

      /// <summary>
      /// Determines whether an exception represents a transient Dataverse 
      /// error.
      /// </summary>
      /// <param name="ex">The exception to check.</param>
      /// <returns>True if the error is transient; otherwise, false.</returns>
      public static bool IsTransientError(Exception ex)
      {
         if (ex == null)
            return false;

         var msg = ex.Message.ToLower();

         bool isTransient =
            msg.Contains(ErrorCodes.ServiceProtectionLimit) ||
            msg.Contains(ErrorCodes.ConnectionTimeout) ||
            msg.Contains(ErrorKeywords.TooManyRequests) ||
            msg.Contains(ErrorKeywords.ExceededLimit) ||
            msg.Contains(ErrorKeywords.CombinedExecutionTime) ||
            msg.Contains(ErrorKeywords.GenericSqlError) ||
            msg.Contains(ErrorKeywords.Timeout);

         if (isTransient)
            return true;

         return ex.InnerException != null &&
            IsTransientError(ex.InnerException);
      }
   }
}


// --- Source: dvmig.Core\Shared\LocalizationService.cs ---

namespace dvmig.Core.Shared
{
   /// <summary>
   /// Provides localization services by loading translations from embedded 
   /// JSON resources.
   /// </summary>
   public static class LocalizationService
   {
      private static Dictionary<string, string> _translations = new();
      private static string _currentLanguage = "en";

      /// <summary>
      /// Initializes the localization service for the specified language.
      /// </summary>
      /// <param name="languageCode">The language code (e.g., "en", "sv").
      /// </param>
      public static void Initialize(string languageCode)
      {
         _currentLanguage = languageCode?.ToLowerInvariant() ?? "en";

         if (_currentLanguage == "en")
         {
            _translations.Clear();

            return;
         }

         LoadTranslations();
      }

      /// <summary>
      /// Translates the specified text.
      /// </summary>
      /// <param name="text">The text to translate.</param>
      /// <returns>The translated text, or the original text if no translation 
      /// is found.</returns>
      public static string Translate(string text)
      {
         if (string.IsNullOrEmpty(text) || _currentLanguage == "en")
            return text;

         return _translations.TryGetValue(text, out var translated) 
            ? translated 
            : text;
      }

      private static void LoadTranslations()
      {
         var assembly = Assembly.GetExecutingAssembly();
         var resourceName = 
            $"dvmig.Core.Resources.lang-{_currentLanguage}.json";


         if (stream == null)
            return;

         try
         {
            var json = reader.ReadToEnd();

            _translations = 
               JsonSerializer.Deserialize<Dictionary<string, string>>(json) ??
               new Dictionary<string, string>();
         }
         catch
         {
            // Fallback to empty dictionary on error
            _translations = new Dictionary<string, string>();
         }
      }
   }
}


// --- Source: dvmig.Core\Shared\Logger.cs ---
#pragma warning disable IDE0005 // Using directive is unnecessary.
#pragma warning restore IDE0005 // Using directive is unnecessary.

namespace dvmig.Core.Shared
{
   /// <summary>
   /// Implementation of <see cref="Interfaces.ILogger"/> that uses Serilog
   /// for file and debug logging, and optionally reports progress to an
   /// <see cref="IProgress{T}"/>.
   /// </summary>
   public class Logger : Interfaces.ILogger
   {
      private readonly SerilogLogger _serilog;
      private IProgress<string>? _progress;

      /// <summary>
      /// Initializes a new instance of the <see cref="Logger"/> class.
      /// </summary>
      public Logger()
      {
         var appData = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData
         );

         var logPath = Path.Combine(
            appData,
            SystemConstants.AppConstants.AppName,
            "logs",
            $"{SystemConstants.AppConstants.AppName}.log"
         );

         _serilog = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Debug()
            .WriteTo.File(
               logPath,
               rollingInterval: RollingInterval.Day,
               flushToDiskInterval: TimeSpan.FromSeconds(1),
               outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} " +
                  "[{Level:u3}] {Message:lj}{NewLine}{Exception}"
            )
            .CreateLogger();
      }

      /// <inheritdoc />
      public void AttachProgress(IProgress<string> progress)
      {
         _progress = progress;
      }

      /// <inheritdoc />
      public void DetachProgress()
      {
         _progress = null;
      }

      /// <inheritdoc />
      public void Debug(string message)
      {
         _serilog.Debug(message);
         // Debug messages usually don't go to progress
      }

      /// <inheritdoc />
      public void Debug(string messageTemplate, params object[] propertyValues)
      {
         _serilog.Debug(messageTemplate, propertyValues);
      }

      /// <inheritdoc />
      public void Information(string message)
      {
         _serilog.Information(message);
         _progress?.Report(message);
      }

      /// <inheritdoc />
      public void Information(
         string messageTemplate, 
         params object[] propertyValues
      )
      {
         _serilog.Information(messageTemplate, propertyValues);
         ReportWithFormatting(messageTemplate, propertyValues);
      }

      /// <inheritdoc />
      public void Warning(string message)
      {
         _serilog.Warning(message);
         _progress?.Report(message);
      }

      /// <inheritdoc />
      public void Warning(
         string messageTemplate, 
         params object[] propertyValues
      )
      {
         _serilog.Warning(messageTemplate, propertyValues);
         ReportWithFormatting(messageTemplate, propertyValues);
      }

      /// <inheritdoc />
      public void Warning(
         Exception ex, 
         string messageTemplate, 
         params object[] propertyValues
      )
      {
         _serilog.Warning(ex, messageTemplate, propertyValues);
         ReportWithFormatting(messageTemplate, propertyValues);
      }

      /// <inheritdoc />
      public void Error(string message)
      {
         _serilog.Error(message);
         _progress?.Report(message);
      }

      /// <inheritdoc />
      public void Error(
         string messageTemplate, 
         params object[] propertyValues
      )
      {
         _serilog.Error(messageTemplate, propertyValues);
         ReportWithFormatting(messageTemplate, propertyValues);
      }

      /// <inheritdoc />
      public void Error(
         Exception ex, 
         string messageTemplate, 
         params object[] propertyValues
      )
      {
         _serilog.Error(ex, messageTemplate, propertyValues);
         ReportWithFormatting(messageTemplate, propertyValues);
      }

      private void ReportWithFormatting(
         string messageTemplate, 
         object[] propertyValues
      )
      {
         if (_progress == null)
            return;

         try
         {
            if (propertyValues == null || propertyValues.Length == 0)
            {
               _progress.Report(messageTemplate);

               return;
            }

            int index = 0;
            var formattedTemplate = Regex.Replace(
               messageTemplate, 
               @"\{([a-zA-Z_][a-zA-Z0-9_]*)\}", 
               m => $"{{{index++}}}"
            );

            var formatted = string.Format(formattedTemplate, propertyValues);

            _progress.Report(formatted);
         }
         catch
         {
            _progress.Report(messageTemplate);
         }
      }
   }
}


// --- Source: dvmig.Core\Shared\Polyfills.cs ---
namespace System.Runtime.CompilerServices
{
   /// <summary>
   /// Polyfill for record support in .NET Standard 2.0 and other legacy 
   /// frameworks.
   /// </summary>
   internal static class IsExternalInit { }
}


// --- Source: dvmig.Core\Shared\StringExtensions.cs ---
namespace dvmig.Core.Shared
{
   /// <summary>
   /// Provides extension methods for string localization.
   /// </summary>
   public static class StringExtensions
   {
      /// <summary>
      /// Translates the string using the LocalizationService.
      /// </summary>
      /// <param name="text">The text to translate.</param>
      /// <param name="args">Optional arguments for string formatting.</param>
      /// <returns>The translated (and optionally formatted) string.</returns>
      public static string t(this string text, params object[] args)
      {
         var translated = LocalizationService.Translate(text);

         if (args == null || args.Length == 0)
            return translated;

         try
         {
            return string.Format(translated, args);
         }
         catch
         {
            // If formatting fails, return the translated (or original) 
            // string as is
            return translated;
         }
      }
   }
}


// --- Source: dvmig.Core\Shared\SystemConstants.cs ---

namespace dvmig.Core.Shared
{
   /// <summary>
   /// Centralized repository for custom Dataverse entity and attribute names 
   /// used by the dvmig tool.
   /// </summary>
   public static class SystemConstants
   {
      public enum ConnectionDirection
      {
         Source,
         Target
      }

      public class DataverseEntity(
         string name,
         bool isSystemEntity,
         bool isActivityEntity
      )
      {
         public string Name { get; } = name;
         public bool IsSystemEntity { get; } = isSystemEntity;
         public bool IsActivityEntity { get; } = isActivityEntity;
      }

      /// <summary>
      /// Logical names for common Dataverse entities.
      /// </summary>
      public static class DataverseEntities
      {
         public static readonly DataverseEntity Account =
            new("account", false, false);

         public static readonly DataverseEntity Contact =
            new("contact", false, false);

         public static readonly DataverseEntity Task =
            new("task", false, true);

         public static readonly DataverseEntity PhoneCall =
            new("phonecall", false, true);

         public static readonly DataverseEntity Appointment =
            new("appointment", false, true);

         public static readonly DataverseEntity Email =
            new("email", false, true);

         public static readonly DataverseEntity EmailTemplate =
            new("template", true, false);

         public static readonly DataverseEntity Fax =
            new("fax", true, true);

         public static readonly DataverseEntity Queue =
            new("queue", true, false);

         public static readonly DataverseEntity Opportunity =
            new("opportunity", true, false);

         public static readonly DataverseEntity Quote =
            new("quote", true, false);

         public static readonly DataverseEntity Order =
            new("salesorder", true, false);

         public static readonly DataverseEntity Invoice =
            new("invoice", true, false);

         public static readonly DataverseEntity Product =
            new("product", true, false);

         public static readonly DataverseEntity Category =
            new("category", true, false);

         public static readonly DataverseEntity Case =
            new("incident", true, false);

         public static readonly DataverseEntity Entitlement =
            new("entitlement", true, false);

         public static readonly DataverseEntity Campaign =
            new("campaign", true, false);

         public static readonly DataverseEntity CampaignActivity =
            new("campaignactivity", true, true);

         public static readonly DataverseEntity MarketingList =
            new("list", true, false);

         public static readonly DataverseEntity PriceLevel =
            new("pricelevel", true, false);

         public static readonly DataverseEntity SystemUser =
            new("systemuser", true, false);

         public static readonly DataverseEntity ActivityParty =
            new("activityparty", true, false);

         /// <summary>
         /// Returns all entities defined in this class as a list, 
         /// sorted by system/activity status and then by name.
         /// </summary>
         public static List<DataverseEntity> ToList()
         {
            return typeof(DataverseEntities)
               .GetFields(
                  BindingFlags.Public |
                  BindingFlags.Static |
                  BindingFlags.FlattenHierarchy
               )
               .Where(f => f.FieldType == typeof(DataverseEntity))
               .Select(f => (DataverseEntity?)f.GetValue(null))
               .Where(e => e != null)
               .Select(e => e!)
               .OrderBy(e => e.IsSystemEntity)
               .ThenBy(e => e.IsActivityEntity)
               .ThenBy(e => e.Name)
               .ToList();
         }
      }

      /// <summary>
      /// Constants for the source data preservation entity.
      /// </summary>
      public static class SourceData
      {
         public const string EntityLogicalName = "dm_sourcedata";
         public const string PrimaryId = "dm_sourcedataid";
         public const string Name = "dm_name";
         public const string EntityId = "dm_sourceentityid";
         public const string EntityLogicalNameAttr =
            "dm_sourceentitylogicalname";
         public const string CreatedOn = "dm_sourcecreateddate";
         public const string ModifiedOn = "dm_sourcemodifieddate";
      }

      /// <summary>
      /// Constants for the migration failure logging entity.
      /// </summary>
      public static class MigrationFailure
      {
         public const string EntityLogicalName = "dm_migrationfailure";
         public const string PrimaryId = "dm_migrationfailureid";
         public const string Name = "dm_name";
         public const string SourceId = "dm_sourceid";
         public const string EntityLogicalNameAttr = "dm_entitylogicalname";
         public const string ErrorMessage = "dm_errormessage";
         public const string Timestamp = "dm_timestamp";
         public const string NotAvailable = "N/A";
      }

      /// <summary>
      /// Common Dataverse attribute names.
      /// </summary>
      public static class DataverseAttributes
      {
         public const string StateCode = "statecode";
         public const string StatusCode = "statuscode";
         public const string CreatedOn = "createdon";
         public const string ModifiedOn = "modifiedon";
         public const string Count = "count";
         public const string InternalEmailAddress = "internalemailaddress";
         public const string DomainName = "domainname";
         public const string SystemUserId = "systemuserid";
         public const string FullName = "fullname";
         public const string OwnerId = "ownerid";
         public const string CreatedBy = "createdby";
         public const string ModifiedBy = "modifiedby";
         public const string CreatedOnBehalfBy = "createdonbehalfby";
         public const string ModifiedOnBehalfBy = "modifiedonbehalfby";
         public const string OverriddenCreatedOn = "overriddencreatedon";
         public const string ImportSequenceNumber = "importsequencenumber";
         public const string VersionNumber = "versionnumber";
         public const string TransactionCurrencyId = "transactioncurrencyid";
         public const string ExchangeRate = "exchangerate";
         public const string ActivityPartyId = "activitypartyid";
         public const string ActivityId = "activityid";
         public const string PartyId = "partyid";
         public const string ParticipationTypeMask = "participationtypemask";
         public const string AddressUsed = "addressused";
         public const string Address1Id = "address1_addressid";
         public const string Address2Id = "address2_addressid";
         public const string Name = "name";
         public const string Telephone1 = "telephone1";
         public const string Address1Line1 = "address1_line1";
         public const string Address1City = "address1_city";
         public const string Address1PostalCode = "address1_postalcode";
         public const string WebsiteUrl = "websiteurl";
         public const string IsDisabled = "isdisabled";
         public const string AccessMode = "accessmode";
         public const string FirstName = "firstname";
         public const string LastName = "lastname";
         public const string EmailAddress1 = "emailaddress1";
         public const string JobTitle = "jobtitle";
         public const string Subject = "subject";
         public const string Description = "description";
         public const string ScheduledEnd = "scheduledend";
         public const string PhoneNumber = "phonenumber";
         public const string RegardingObjectId = "regardingobjectid";
         public const string To = "to";
         public const string ParentCustomerId = "parentcustomerid";
         public const string PrimaryContactId = "primarycontactid";
      }

      /// <summary>
      /// Attributes that should not be copied from source to target.
      /// </summary>
      public static readonly HashSet<string> ForbiddenAttributes =
         new(StringComparer.OrdinalIgnoreCase)
         {
            DataverseAttributes.CreatedOn,
            DataverseAttributes.ModifiedOn,
            DataverseAttributes.CreatedBy,
            DataverseAttributes.ModifiedBy,
            DataverseAttributes.CreatedOnBehalfBy,
            DataverseAttributes.ModifiedOnBehalfBy,
            DataverseAttributes.OverriddenCreatedOn,
            DataverseAttributes.ImportSequenceNumber,
            DataverseAttributes.VersionNumber,
            DataverseAttributes.Address1Id,
            DataverseAttributes.Address2Id
         };

      /// <summary>
      /// Attributes that represent references to SystemUser records.
      /// </summary>
      public static readonly HashSet<string> UserAttributes =
         new(StringComparer.OrdinalIgnoreCase)
         {
            DataverseAttributes.OwnerId,
            DataverseAttributes.CreatedBy,
            DataverseAttributes.ModifiedBy,
            DataverseAttributes.CreatedOnBehalfBy,
            DataverseAttributes.ModifiedOnBehalfBy
         };

      /// <summary>
      /// Keywords found in Dataverse error messages for pattern matching.
      /// </summary>
      public static class ErrorKeywords
      {
         public const string AlreadyExists = "already exists";
         public const string DuplicateCurrency = "duplicate currency record";
         public const string DuplicateKey = "duplicate key";
         public const string InvalidStatusCode = "is not a valid status code";
         public const string DoesNotExist = "does not exist";
         public const string ForeignKeyConflict =
            "conflicted with the foreign key constraint";
         public const string ConstraintConflict =
            "conflicted with a constraint";
         public const string CannotBeModified = "cannot be modified";
         public const string CannotBeSetOnCreation =
            "cannot be set on creation";
         public const string OutsideValidRange = "outside the valid range";
         public const string TooManyRequests = "too many requests";
         public const string ExceededLimit = "exceeded the limit";
         public const string CombinedExecutionTime = "combined execution time";
         public const string GenericSqlError = "generic sql error";
         public const string Timeout = "timeout";
      }

      /// <summary>
      /// Specific Dataverse error codes.
      /// </summary>
      public static class ErrorCodes
      {
         public const string ServiceProtectionLimit = "8004410d";
         public const string ConnectionTimeout = "80072321";
         public const string DoesNotExist = "80040217";
      }

      /// <summary>
      /// Keywords used to identify sensitive information for masking.
      /// </summary>
      public static class MaskingKeywords
      {
         public const string Password = "password";
         public const string Secret = "secret";
         public const string Token = "token";
         public const string Thumbprint = "thumbprint";
         public const string ClientId = "clientid";
         public const string AppId = "appid";
         public const string UserId = "userid";
         public const string Username = "username";
      }

      /// <summary>
      /// Settings related to data synchronization.
      /// </summary>
      public static class SyncSettings
      {
         public static readonly int[] ParallelismOptions =
            new[]
            {
               1,
               5,
               10,
               15,
               20
            };

         public static IReadOnlyList<string> RecommendedEntities =>
            DataverseEntities.ToList()
               .Where(e => !e.IsSystemEntity)
               .Select(e => e.Name)
               .ToList()
               .AsReadOnly();
      }

      /// <summary>
      /// Spectre.Console markup and UI keywords.
      /// </summary>
      public static class UiMarkup
      {
         public const string Yellow = "[yellow]";
         public const string Red = "[red]";
         public const string Grey = "[grey]";
         public const string Green = "[green]";
         public const string Blue = "[blue]";
         public const string Cyan = "[cyan]";
         public const string BoldYellow = "[bold yellow]";
         public const string BoldRed = "[bold red]";
         public const string BoldGreen = "[bold green]";
         public const string BoldBlue = "[bold blue]";
         public const string BoldCyan = "[bold cyan]";
         public const string BoldMagenta = "[bold magenta]";
         public const string Wait = "WAIT";
         public const string WipeDataConfirmation = "WIPE DATA";
      }

      /// <summary>
      /// Constants for Dataverse plugin registration entities and attributes.
      /// </summary>
      public static class PluginRegistration
      {
         public const string AssemblyEntity = "pluginassembly";
         public const string AssemblyId = "pluginassemblyid";
         public const string AssemblyName = "name";
         public const string Content = "content";
         public const string IsolationMode = "isolationmode";
         public const string SourceType = "sourcetype";
         public const string PublicKeyToken = "publickeytoken";
         public const string Version = "version";
         public const string Culture = "culture";

         public const string TypeEntity = "plugintype";
         public const string TypeId = "plugintypeid";
         public const string TypeName = "typename";
         public const string FriendlyName = "friendlyname";

         public const string StepEntity = "sdkmessageprocessingstep";
         public const string StepId = "sdkmessageprocessingstepid";
         public const string Configuration = "configuration";
         public const string InvocationSource = "invocationsource";
         public const string Stage = "stage";
         public const string SupportedDeployment = "supporteddeployment";
         public const string Rank = "rank";
         public const string Mode = "mode";

         public const string EventHandler = "eventhandler";

         public const string MessageEntity = "sdkmessage";
         public const string MessageId = "sdkmessageid";
         public const string MessageName = "name";
      }

      /// <summary>
      /// Constants related to Dataverse connections.
      /// </summary>
      public static class Connection
      {
         public const string UnknownEnvironment = "Unknown Environment";
         public const string Url = "Url";
         public const string ServiceUri = "ServiceUri";
         public const string Server = "Server";
         public const string Https = "https://";
         public const string Http = "http://";
      }

      /// <summary>
      /// Application-wide constants for configuration, batch sizes, 
      /// and file names.
      /// </summary>
      public static class AppConstants
      {
         public const int DefaultDeletionBatchSize = 5000;
         public const int MetadataPropagationDelayMs = 5000;
         public const int MaxMemoFieldLength = 5000;
         public const int RecordFetchLimit = 100;
         public const string AppName = "dvmig";
         public const string SettingsFileName = "settings.json";
         public const string StateFolderName = "state";
         public const string PluginAssemblyName = "dvmig.Plugins.dll";
         public const string PluginName = "dvmig.Plugins";
      }
   }
}


// --- Source: dvmig.Core\Synchronization\EntityService.cs ---

namespace dvmig.Core.Synchronization
{
   /// <summary>
   /// Implementation of <see cref="IEntityService"/> that handles 
   /// entity preparation, metadata management, and record associations.
   /// </summary>
   public class EntityService : IEntityService
   {
      private readonly ILogger _logger;
      private readonly IDataverseProvider? _target;

      private readonly ConcurrentDictionary<string, EntityMetadata> _metaCache =
         new ConcurrentDictionary<string, EntityMetadata>();

      /// <summary>
      /// Initializes a new instance of the <see cref="EntityService"/> class.
      /// </summary>
      /// <param name="logger">The logger instance.</param>
      /// <param name="target">
      /// Optional target provider for cached metadata operations.
      /// </param>
      public EntityService(ILogger logger, IDataverseProvider? target = null)
      {
         _logger = logger;
         _target = target;
      }

      #region IEntityService Implementation (Preparation)

      /// <inheritdoc />
      public async Task<Entity> PrepareEntityForTargetAsync(
         Entity sourceEntity,
         EntityMetadata metadata,
         SyncOptions options,
         IUserService userResolver,
         ConcurrentDictionary<string, Guid> idMappingCache,
         CancellationToken ct = default
      )
      {
         var targetEntity = new Entity(
            sourceEntity.LogicalName,
            sourceEntity.Id
         );

         foreach (var attribute in sourceEntity.Attributes)
         {
            if (ForbiddenAttributes.Contains(attribute.Key))
               continue;

            var attrMetadata = metadata.Attributes?
               .FirstOrDefault(a => a.LogicalName == attribute.Key);

            if (attrMetadata != null &&
                attrMetadata.IsValidForCreate == false &&
                attrMetadata.IsValidForUpdate == false)
               continue;

            var value = attribute.Value;

            if (value is EntityReference er)
            {
               if (UserAttributes.Contains(attribute.Key))
               {
                  value = await userResolver.MapUserAsync(er, ct);

                  if (value == null)
                  {
                     _logger.Warning(
                        "Skipping unmapped user field {Attr} for " +
                        "{Entity}:{Id}; source user {UserId} was " +
                        "not found or could not be resolved.",
                        attribute.Key,
                        sourceEntity.LogicalName,
                        sourceEntity.Id,
                        er.Id
                     );

                     continue;
                  }
               }
               else if (idMappingCache.TryGetValue(
                  EntityHelper.GetRecordKey(er),
                  out var mappedId))
                  value = new EntityReference(er.LogicalName, mappedId);
            }
            else if (value is EntityCollection collection &&
                     collection.Entities.Count > 0 &&
                     collection.Entities[0].LogicalName ==
                        SystemConstants.DataverseEntities.ActivityParty.Name)
            {
               value = await PrepareActivityPartyCollectionAsync(
                  collection,
                  sourceEntity,
                  userResolver,
                  idMappingCache,
                  ct
               );
            }

            targetEntity[attribute.Key] = value;
         }

         targetEntity[SystemConstants.DataverseAttributes
            .ImportSequenceNumber] = 1;

         return targetEntity;
      }

      private async Task<EntityCollection> PrepareActivityPartyCollectionAsync(
         EntityCollection collection,
         Entity sourceEntity,
         IUserService userResolver,
         ConcurrentDictionary<string, Guid> idMappingCache,
         CancellationToken ct
      )
      {
         var newCollection = new EntityCollection
         {
            EntityName = collection.EntityName
         };

         foreach (var party in collection.Entities)
         {
            var targetParty = new Entity(
               SystemConstants.DataverseEntities.ActivityParty.Name
            );
            bool skipParty = false;

            foreach (var partyAttr in party.Attributes)
            {
               bool isPartyId = partyAttr.Key ==
                  SystemConstants.DataverseAttributes.PartyId;
               bool isTypeMask = partyAttr.Key ==
                  SystemConstants.DataverseAttributes
                     .ParticipationTypeMask;
               bool isAddress = partyAttr.Key ==
                  SystemConstants.DataverseAttributes.AddressUsed;

               if (!isPartyId && !isTypeMask && !isAddress)
                  continue;

               var partyValue = partyAttr.Value;

               if (partyAttr.Key ==
                      SystemConstants.DataverseAttributes.PartyId &&
                   partyValue is EntityReference pr)
               {
                  if (pr.LogicalName ==
                         SystemConstants.DataverseEntities.SystemUser.Name)
                  {
                     partyValue = await userResolver.MapUserAsync(pr, ct);

                     if (partyValue == null)
                     {
                        _logger.Warning(
                           "Skipping unmapped user partyid for " +
                           "{Entity}:{Id}; source user {UserId} was " +
                           "not found or could not be resolved.",
                           sourceEntity.LogicalName,
                           sourceEntity.Id,
                           pr.Id
                        );

                        skipParty = true;
                        break;
                     }
                  }
                  else if (idMappingCache.TryGetValue(
                     EntityHelper.GetRecordKey(pr),
                     out var mappedId))
                  {
                     partyValue = new EntityReference(
                        pr.LogicalName,
                        mappedId
                     );
                  }
               }

               targetParty[partyAttr.Key] = partyValue;
            }

            if (!skipParty)
               newCollection.Entities.Add(targetParty);
         }

         return newCollection;
      }

      /// <inheritdoc />
      public async Task<Guid?> FindExistingOnTargetAsync(
         Entity entity,
         IDataverseProvider target,
         CancellationToken ct = default
      )
      {
         var metadata = await GetMetadataAsync(entity.LogicalName, ct);

         if (metadata == null)
            return null;

         var primaryNameAttr = metadata.PrimaryNameAttribute;

         if (string.IsNullOrEmpty(primaryNameAttr) ||
             !entity.Contains(primaryNameAttr))
            return null;

         var query = new QueryByAttribute(entity.LogicalName)
         {
            ColumnSet = new ColumnSet(metadata.PrimaryIdAttribute)
         };

         query.AddAttributeValue(primaryNameAttr, entity[primaryNameAttr]);

         var results = await target.RetrieveMultipleAsync(query, ct);

         return results.Entities.FirstOrDefault()?.Id;
      }

      /// <inheritdoc />
      public async Task<HashSet<Guid>> GetAllIdsAsync(
         IDataverseProvider provider,
         string logicalName,
         CancellationToken ct = default
      )
      {
         var query = new QueryExpression(logicalName)
         {
            ColumnSet = new ColumnSet(false)
         };

         var results = await provider.RetrieveMultipleAsync(query, ct);

         return new HashSet<Guid>(results.Entities.Select(e => e.Id));
      }

      #endregion

      #region IEntityService Implementation (Metadata)

      /// <inheritdoc />
      public async Task<EntityMetadata?> GetMetadataAsync(
         string entityLogicalName,
         CancellationToken ct = default
      )
      {
         if (_metaCache.TryGetValue(entityLogicalName, out var meta))
            return meta;

         if (_target == null)
            throw new InvalidOperationException(
               "Target provider is required for cached metadata retrieval."
            );

         try
         {
            var newMeta = await _target
               .GetEntityMetadataAsync(entityLogicalName, ct);

            if (newMeta != null)
               _metaCache[entityLogicalName] = newMeta;

            return newMeta;
         }
         catch (Exception ex)
         {
            _logger.Warning(
               "Could not fetch metadata for {Entity}: {Msg}",
               entityLogicalName,
               ex.Message
            );

            return null;
         }
      }

      /// <inheritdoc />
      public async Task<ColumnSet> GetValidColumnsAsync(
         string logicalName,
         CancellationToken ct = default
      )
      {
         var meta = await GetMetadataAsync(logicalName, ct);

         if (meta == null || meta.Attributes == null)
            return new ColumnSet(true);

         var whitelist = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase
         )
         {
            meta.PrimaryIdAttribute,
            meta.PrimaryNameAttribute ?? string.Empty,
            DataverseAttributes.OwnerId,
            DataverseAttributes.StateCode,
            DataverseAttributes.StatusCode,
            DataverseAttributes.CreatedOn,
            DataverseAttributes.ModifiedOn,
            DataverseAttributes.TransactionCurrencyId,
            DataverseAttributes.ExchangeRate,
            DataverseAttributes.CreatedBy,
            DataverseAttributes.ModifiedBy
         };

         var attributes = meta.Attributes
            .Where(a =>
               whitelist.Contains(a.LogicalName) ||
               a.AttributeType == AttributeTypeCode.PartyList ||
               (a.IsLogical == false &&
                a.IsValidForRead == true &&
                (a.IsValidForCreate == true ||
                 a.IsValidForUpdate == true)))
            .Select(a => a.LogicalName)
            .Where(name => !string.IsNullOrEmpty(name))
            .Distinct()
            .ToArray();

         if (attributes.Length == 0)
            return new ColumnSet(true);

         _logger.Debug(
            "Configured ColumnSet for {Entity} with {Count} attributes.",
            logicalName,
            attributes.Length
         );

         return new ColumnSet(attributes);
      }

      /// <inheritdoc />
      public async Task<List<EntityMetadata>> GetMigrationEntitiesAsync(
         IDataverseProvider provider,
         CancellationToken ct = default
      )
      {
         var request = new RetrieveAllEntitiesRequest
         {
            EntityFilters = EntityFilters.Entity,
            RetrieveAsIfPublished = true
         };

         var response = (RetrieveAllEntitiesResponse)await
            provider.ExecuteAsync(request, ct);

         var entities = response.EntityMetadata
            .Where(e =>
               e.IsIntersect == false &&
               e.IsValidForAdvancedFind == true &&
               e.IsImportable == true &&
               e.IsLogicalEntity == false)
            .OrderBy(e =>
               e.DisplayName?.UserLocalizedLabel?.Label ??
               e.LogicalName)
            .ToList();

         return entities;
      }

      /// <inheritdoc />
      public void ClearMetadataCache()
      {
         _metaCache.Clear();
      }

      #endregion

      #region IEntityService Implementation (Relationships)

      /// <inheritdoc />
      public async Task AssociateAsync(
         IDataverseProvider target,
         Entity entity,
         CancellationToken ct = default,
         Guid? callerId = null
      )
      {
         var references = entity.Attributes
            .Values.OfType<EntityReference>().ToList();

         if (references.Count < 2)
         {
            _logger.Warning(
               "Intersect entity {Key} does not have " +
               "two EntityReferences.",
               entity.LogicalName
            );

            throw new System.InvalidOperationException(
               "Invalid N:N relationship record."
            );
         }

         var request = new AssociateRequest
         {
            Target = references[0],
            Relationship = new Relationship(entity.LogicalName),
            RelatedEntities = new EntityReferenceCollection
            {
               references[1]
            }
         };

         await target.ExecuteAsync(request, ct, callerId);
      }

      #endregion
   }
}


// --- Source: dvmig.Core\Synchronization\MigrationFailureRecord.cs ---
namespace dvmig.Core.Synchronization
{
   /// <summary>
   /// Represents a recorded migration failure.
   /// </summary>
   public class MigrationFailureRecord
   {
      public Guid Id { get; set; }
      public string EntityLogicalName { get; set; } = string.Empty;
      public string SourceId { get; set; } = string.Empty;
      public string ErrorMessage { get; set; } = string.Empty;
      public DateTime TimestampUtc { get; set; }
   }
}


// --- Source: dvmig.Core\Synchronization\SyncEngine.cs ---



namespace dvmig.Core.Synchronization
{
   /// <summary>
   /// Core orchestrator responsible for synchronizing data records between
   /// source and target Dataverse environments, managing both high-level
   /// pagination and record-level resilience.
   /// </summary>
   public class SyncEngine : ISyncEngine
   {
      private readonly IDataverseProvider _source;
      private readonly IDataverseProvider _target;
      private readonly IUserService _userResolver;
      private readonly ILogger _logger;
      private readonly IEntityService _entityService;
      private readonly ISyncStateService _syncStateService;

      private bool? _isSourceDataSupported;
      private const int MaxRecursionDepth = 5;

      /// <summary>
      /// Initializes a new instance of the <see cref="SyncEngine"/> class.
      /// </summary>
      public SyncEngine(
         IDataverseProvider source,
         IDataverseProvider target,
         IUserService userResolver,
         ILogger logger,
         IEntityService entityService,
         ISyncStateService syncStateService
      )
      {
         _source = source;
         _target = target;
         _userResolver = userResolver;
         _logger = logger;
         _entityService = entityService;
         _syncStateService = syncStateService;
      }

      #region Entity Sync Orchestration (Batch)

      /// <inheritdoc />
      public async Task InitializeEntitySyncAsync(
         string logicalName,
         CT ct = default
      )
      {
         var ids = await _entityService.GetAllIdsAsync(
            _target,
            logicalName,
            ct
         );

         _syncStateService.InitializeSyncedIds(ids);
      }

      /// <inheritdoc />
      public async Task SyncAsync(
         string logicalName,
         SyncOptions options,
         QueryExpression? query = null,
         IProgress<bool>? recordProgress = null,
         CT ct = default
      )
      {
         await InitializeEntitySyncAsync(logicalName, ct);

         var columns = await _entityService.GetValidColumnsAsync(
            logicalName,
            ct
         );

         var syncQuery = query ?? new QueryExpression(logicalName)
         {
            ColumnSet = columns
         };

         syncQuery.PageInfo = new PagingInfo
         {
            Count = 500,
            PageNumber = 1
         };

         int totalSynced = 0;

         while (true)
         {
            var response = await _source.RetrieveMultipleAsync(
               syncQuery,
               ct
            );

            if (response.Entities.Count == 0)
               break;

            var count = response.Entities.Count;
            var recordCountSoFar = totalSynced + count;

            _logger.Information(
               $"Syncing {logicalName} page " +
               $"{syncQuery.PageInfo.PageNumber} " +
               $"({recordCountSoFar} records " +
               $"found so far)..."
            );

            await ProcessBatchAsync(
               response.Entities,
               options,
               recordProgress,
               ct
            );

            totalSynced += response.Entities.Count;

            if (!response.MoreRecords)
               break;

            syncQuery.PageInfo.PageNumber++;
            syncQuery.PageInfo.PagingCookie = response.PagingCookie;
         }

         _logger.Information(
            "SyncEntity {Entity} finished. Total records: {Count}",
            logicalName,
            totalSynced
         );

         _syncStateService.ClearState();
      }

      private async Task ProcessBatchAsync(
         IEnumerable<Entity> entities,
         SyncOptions options,
         IProgress<bool>? recordProgress = null,
         CT ct = default
      )
      {
         var entitiesToSync = options.ForceResync
            ? entities.ToList()
            : entities
               .Where(e => !_syncStateService.IsSynced(e.Id))
               .ToList();

         if (!entitiesToSync.Any())
            return;

         var parallelOptions = new ParallelOptions
         {
            MaxDegreeOfParallelism = options.MaxDegreeOfParallelism,
            CancellationToken = ct
         };

         using (
            var semaphore = new SemaphoreSlim(
               parallelOptions.MaxDegreeOfParallelism
            )
         )
         {
            var tasks = entitiesToSync.Select(async entity =>
            {
               await semaphore.WaitAsync(ct);

               try
               {
                  await SyncRecordAndReportAsync(
                     entity,
                     options,
                     recordProgress,
                     ct
                  );
               }
               finally
               {
                  semaphore.Release();
               }
            });

            await Task.WhenAll(tasks);
         }
      }

      #endregion

      #region Record Sync Workflow

      /// <inheritdoc />
      public async Task SyncRecordAndReportAsync(
         Entity entity,
         SyncOptions options,
         IProgress<bool>? recordProgress,
         CT ct = default
      )
      {
         try
         {
            var (success, failureMessage) = await SyncRecordAsync(
               entity,
               options,
               ct
            );

            if (!success)
               await LogFailureToTargetAsync(
                  entity,
                  failureMessage ?? "Sync failed.",
                  ct
               );

            recordProgress?.Report(success);
         }
         catch (Exception ex)
         {
            _logger.Error(
               ex,
               "Error syncing {Entity}:{Id}",
               entity.LogicalName,
               entity.Id
            );

            await LogFailureToTargetAsync(
               entity,
               FormatFailureMessage("SyncAsync", ex),
               ct
            );

            recordProgress?.Report(false);
         }
      }

      /// <inheritdoc />
      public async Task<(bool Success, string? FailureMessage)>
         SyncRecordAsync(
            Entity entity,
            SyncOptions options,
            CT ct = default
         )
      {
         if (!options.ForceResync && _syncStateService.IsSynced(entity.Id))
            return (true, string.Empty);

         var recordKey = EntityHelper.GetRecordKey(entity);

         if (!_syncStateService.TryEnterRecordScope(
            recordKey,
            MaxRecursionDepth
         ))
            return (false, "Max recursion depth reached.");

         try
         {
            return await SyncRecordCoreAsync(entity, options, ct);
         }
         finally
         {
            _syncStateService.LeaveRecordScope(recordKey);
         }
      }

      private async Task<(bool Success, string? FailureMessage)>
         SyncRecordCoreAsync(
            Entity entity,
            SyncOptions options,
            CT ct
         )
      {
         var metadata = await _entityService.GetMetadataAsync(
            entity.LogicalName,
            ct
         );

         if (metadata == null)
            return (false, $"Metadata missing for {entity.LogicalName}");

         if (metadata.IsIntersect == true)
            return await SyncIntersectEntityAsync(entity, options, ct);

         var prepared = await _entityService.PrepareEntityForTargetAsync(
            entity,
            metadata,
            options,
            _userResolver,
            _syncStateService.IdMappingCache,
            ct
         );

         Guid? creatorId = null;
         Guid? modifiedById = null;

         if (options.PreserveAuditData)
         {
            var sourceCreator = entity.GetAttributeValue<EntityReference>(
               DataverseAttributes.CreatedBy
            );

            if (sourceCreator != null)
            {
               var user = await _userResolver.MapUserAsync(sourceCreator, ct);
               creatorId = user?.Id;
            }

            var sourceModifier = entity.GetAttributeValue<EntityReference>(
               DataverseAttributes.ModifiedBy
            );

            if (sourceModifier != null)
               modifiedById = (await _userResolver.MapUserAsync(
                  sourceModifier,
                  ct
               ))?.Id;

            creatorId ??= modifiedById;
            modifiedById ??= creatorId;
         }

         await PreserveAuditDataIfRequestedAsync(entity, options, ct);

         var (success, failureMessage) = await CreateWithFixStrategyAsync(
            prepared,
            entity,
            options,
            creatorId,
            modifiedById,
            ct
         );

         if (!success)
         {
            return (
               false,
               failureMessage ??
                  $"Failed to sync {EntityHelper.GetRecordKey(entity)}."
            );
         }

         await CompleteSuccessfulSyncAsync(
            sourceEntity: entity,
            targetEntity: prepared,
            options,
            ct
         );

         return (true, string.Empty);
      }

      private async Task PreserveAuditDataIfRequestedAsync(
         Entity entity,
         SyncOptions options,
         CT ct
      )
      {
         if (!options.PreserveAuditData)
            return;

         try
         {
            await CreateSourceDataRecordAsync(_target, entity, ct);
         }
         catch (Exception ex)
         {
            _logger.Warning(
               ex,
               "Audit data preservation failed for {Entity}:{Id}",
               entity.LogicalName,
               entity.Id
            );
         }
      }

      private async Task CompleteSuccessfulSyncAsync(
         Entity sourceEntity,
         Entity targetEntity,
         SyncOptions options,
         CT ct
      )
      {
         var recordKey = EntityHelper.GetRecordKey(sourceEntity);

         _syncStateService.MarkAsSynced(sourceEntity.Id);
         _syncStateService.IdMappingCache[recordKey] = targetEntity.Id;

         _logger.Information(
            "Synced {Key}:{Id}",
            sourceEntity.LogicalName,
            sourceEntity.Id
         );

         if (options.PreserveAuditData)
         {
            await DeleteSourceDataRecordAsync(
               _target,
               sourceEntity.LogicalName,
               targetEntity.Id,
               ct
            );
         }
      }

      #endregion

      #region Error Handling & Resilience

      public async Task<(bool Success, string FailureMessage)>
         HandleSyncExceptionAsync(
            Exception ex,
            Entity entity,
            Entity sourceEntity,
            SyncOptions options,
            Guid? creatorId = null,
            Guid? modifiedById = null,
            CT ct = default
         )
      {
         var msg = ex.Message.ToLower();

         bool isDuplicate =
            msg.Contains(ErrorKeywords.AlreadyExists) ||
            msg.Contains(ErrorKeywords.DuplicateCurrency) ||
            msg.Contains(ErrorKeywords.DuplicateKey);

         if (isDuplicate)
            return await HandleDuplicateAsync(entity, modifiedById, ct);

         if (msg.Contains(ErrorKeywords.InvalidStatusCode))
         {
            var success = await HandleStatusTransitionAsync(
               entity,
               sourceEntity,
               options,
               ct,
               modifiedById
            );

            if (success)
               return (true, string.Empty);

            return (
               false,
               FormatFailureMessage("Status transition failed", ex)
            );
         }

         if (msg.Contains(ErrorKeywords.DoesNotExist) ||
             msg.Contains(ErrorKeywords.ForeignKeyConflict))
         {
            var success = await ResolveDependencyAsync(
               ex,
               entity,
               sourceEntity,
               options,
               creatorId,
               modifiedById,
               ct
            );

            if (success)
               return (true, string.Empty);

            return (
               false,
               FormatFailureMessage("Dependency resolution failed", ex)
            );
         }

         if (msg.Contains(ErrorKeywords.CannotBeModified) ||
             msg.Contains(ErrorKeywords.CannotBeSetOnCreation) ||
             msg.Contains(ErrorKeywords.OutsideValidRange))
         {
            var success = await StripAttributeAndRetryAsync(
               ex,
               entity,
               sourceEntity,
               options,
               creatorId,
               modifiedById,
               ct
            );

            if (success)
               return (true, string.Empty);

            return (
               false,
               FormatFailureMessage("Attribute stripping failed", ex)
            );
         }

         _logger.Error(
            ex,
            "Unresolved error for {Key}:{Id}",
            entity.LogicalName,
            entity.Id
         );

         return (false, FormatFailureMessage("Unresolved error", ex));
      }

      private async Task<(bool Success, string FailureMessage)>
         HandleDuplicateAsync(
            Entity entity,
            Guid? modifiedById,
            CT ct
         )
      {
         _logger.Information(
            "{Key}:{Id} already exists. Attempting update.",
            entity.LogicalName,
            entity.Id
         );

         try
         {
            var targetId = await FindExistingOnTargetAsync(entity, ct);

            if (targetId.HasValue && targetId.Value != entity.Id)
            {
               entity.Id = targetId.Value;

               var pk = await _target.GetPrimaryIdAttributeAsync(
                  entity.LogicalName,
                  ct
               ) ?? $"{entity.LogicalName}id";

               if (entity.Attributes.Contains(pk))
                  entity[pk] = targetId.Value;
            }

            await _target.UpdateAsync(entity, ct, modifiedById);

            return (true, string.Empty);
         }
         catch (Exception updateEx)
         {
            _logger.Warning(
               "Update failed for existing record {Key}:{Id}: {Msg}.",
               entity.LogicalName,
               entity.Id,
               updateEx.Message
            );

            return (true, string.Empty);
         }
      }

      private async Task<bool> HandleStatusTransitionAsync(
         Entity entity,
         Entity sourceEntity,
         SyncOptions options,
         CT ct,
         Guid? callerId = null
      )
      {
         var stateValue = entity.Contains(
            DataverseAttributes.StateCode
         )
            ? entity[DataverseAttributes.StateCode]
            : null;

         var statusValue = entity.Contains(
            DataverseAttributes.StatusCode
         )
            ? entity[DataverseAttributes.StatusCode]
            : null;

         entity.Attributes.Remove(DataverseAttributes.StateCode);
         entity.Attributes.Remove(DataverseAttributes.StatusCode);

         var sourceRetry = EntityHelper.Clone(sourceEntity);

         sourceRetry.Attributes.Remove(DataverseAttributes.StateCode);
         sourceRetry.Attributes.Remove(DataverseAttributes.StatusCode);

         var (success, _) = await SyncRecordAsync(
            sourceRetry,
            options.CloneWithForceResync(),
            ct
         );

         if (success && (stateValue != null || statusValue != null))
         {
            try
            {
               var stateOsv = ToOptionSetValue(stateValue);
               var statusOsv = ToOptionSetValue(statusValue);

               if (stateOsv != null)
               {
                  var request = new SetStateRequest
                  {
                     EntityMoniker = entity.ToEntityReference(),
                     State = stateOsv,
                     Status = statusOsv ?? new OptionSetValue(-1)
                  };

                  await _target.ExecuteAsync(request, ct, callerId);
               }
            }
            catch (Exception ex)
            {
               _logger.Warning(
                  "SetState failed for {Key}:{Id}: {Msg}. " +
                  "Trying fallback Update.",
                  entity.LogicalName,
                  entity.Id,
                  ex.Message
               );

               try
               {
                  var fallback = new Entity(entity.LogicalName, entity.Id);

                  if (stateValue != null)
                     fallback[DataverseAttributes.StateCode] =
                        stateValue;

                  if (statusValue != null)
                     fallback[DataverseAttributes.StatusCode] =
                        statusValue;

                  await _target.UpdateAsync(fallback, ct, callerId);
               }
               catch
               {
                  // Final failure ignored
               }
            }
         }

         return success;
      }

      private async Task<bool> ResolveDependencyAsync(
         Exception ex,
         Entity entity,
         Entity sourceEntity,
         SyncOptions options,
         Guid? creatorId = null,
         Guid? modifiedById = null,
         CT ct = default
      )
      {
         if (ex.Message.Contains(ErrorKeywords.ForeignKeyConflict))
         {
            var match = Regex.Match(ex.Message, @"column '(\w+)'");

            if (match.Success)
            {
               var columnName = match.Groups[1].Value.ToLower();
               var attr = entity.Attributes
                  .FirstOrDefault(a => a.Key.ToLower() == columnName);

               if (attr.Value is EntityReference er)
                  return await ResolveDependencyInternalAsync(
                     er.LogicalName,
                     er.Id,
                     entity,
                     sourceEntity,
                     options,
                     creatorId,
                     modifiedById,
                     ct
                  );
            }

            return false;
         }

         var pattern = @"(?:Entity )?'?(\w+)'? [Ww]ith " +
            @"Id\s*=\s*([a-fA-F0-9-]+)";
         var m = Regex.Match(ex.Message, pattern, RegexOptions.IgnoreCase);

         if (!m.Success)
            return false;

         var type = m.Groups[1].Value.ToLower();
         var id = Guid.Parse(m.Groups[2].Value);

         return await ResolveDependencyInternalAsync(
            type,
            id,
            entity,
            sourceEntity,
            options,
            creatorId,
            modifiedById,
            ct
         );
      }

      private async Task<bool> ResolveDependencyInternalAsync(
         string type,
         Guid id,
         Entity parent,
         Entity sourceParent,
         SyncOptions options,
         Guid? creatorId,
         Guid? modifiedById,
         CT ct
      )
      {
         var parentKey = EntityHelper.GetRecordKey(parent);
         var depKey = EntityHelper.GetRecordKey(type, id);

         var tried = _syncStateService.TriedDependencies.GetOrAdd(
            parentKey,
            _ => new HashSet<string>()
         );

         if (tried.Contains(depKey))
         {
            if (options.StripMissingDependencies)
               return await StripSpecificAttributeAsync(
                  type,
                  id,
                  parent,
                  sourceParent,
                  options,
                  ct
               );

            return false;
         }

         tried.Add(depKey);

         _logger.Information("Resolving missing dependency: {0}", depKey);

         var record = await _source.RetrieveAsync(type, id, null, ct);

         if (record != null)
         {
            var (success, _) = await SyncRecordAsync(record, options, ct);

            if (success)
            {
               var res = await SyncRecordAsync(
                  sourceParent,
                  options.CloneWithForceResync(),
                  ct
               );

               return res.Success;
            }
         }

         if (options.StripMissingDependencies)
            return await StripSpecificAttributeAsync(
               type,
               id,
               parent,
               sourceParent,
               options,
               ct
            );

         return false;
      }

      private async Task<bool> StripSpecificAttributeAsync(
         string type,
         Guid id,
         Entity parent,
         Entity sourceParent,
         SyncOptions options,
         CT ct
      )
      {
         var attr = parent.Attributes
            .FirstOrDefault(a =>
               a.Value is EntityReference er &&
               er.LogicalName == type &&
               er.Id == id
            ).Key;

         if (string.IsNullOrEmpty(attr))
            return false;

         _logger.Warning(
            "Stripping missing dependency '{0}' from {1}",
            attr,
            parent.LogicalName
         );

         parent.Attributes.Remove(attr);
         
         var sourceRetry = EntityHelper.Clone(sourceParent);
         sourceRetry.Attributes.Remove(attr);

         return (await SyncRecordAsync(
            sourceRetry,
            options.CloneWithForceResync(),
            ct
         )).Success;
      }

      private async Task<bool> StripAttributeAndRetryAsync(
         Exception ex,
         Entity entity,
         Entity sourceEntity,
         SyncOptions options,
         Guid? creatorId,
         Guid? modifiedById,
         CT ct
      )
      {
         var match = Regex.Match(ex.Message, @"'(\w+)'");

         if (match.Success)
         {
            var attr = match.Groups[1].Value;

            if (entity.Attributes.Contains(attr))
            {
               _logger.Warning("Stripping problematic attribute '{0}'", attr);

               entity.Attributes.Remove(attr);
               
               var sourceRetry = EntityHelper.Clone(sourceEntity);
               sourceRetry.Attributes.Remove(attr);

               var res = await SyncRecordAsync(
                  sourceRetry,
                  options.CloneWithForceResync(),
                  ct
               );

               return res.Success;
            }
         }

         return false;
      }

      private OptionSetValue? ToOptionSetValue(object? value)
      {
         if (value == null)
            return null;

         return value is OptionSetValue osv
            ? osv
            : new OptionSetValue((int)value);
      }

      #endregion

      #region Create and Associate Operations

      private async Task<(bool success, string failureMessage)>
         SyncIntersectEntityAsync(
            Entity entity,
            SyncOptions options,
            CT ct
         )
      {
         Guid? callerId = null;

         if (options.PreserveAuditData)
         {
            var sourceCreator = entity.GetAttributeValue<EntityReference>(
               DataverseAttributes.CreatedBy
            );

            if (sourceCreator != null)
            {
               var user = await _userResolver.MapUserAsync(sourceCreator, ct);

               callerId = user?.Id;
            }
         }

         try
         {
            await _entityService.AssociateAsync(_target, entity, ct, callerId);

            _logger.Information("Associated N:N {Key}", entity.LogicalName);

            return (true, string.Empty);
         }
         catch (Exception ex)
         {
            if (ex.Message.Contains(ErrorKeywords.AlreadyExists))
               return (true, string.Empty);

            return (false, FormatFailureMessage("AssociateAsync", ex));
         }
      }

      private async Task<(bool Success, string FailureMessage)>
         CreateWithFixStrategyAsync(
            Entity preparedEntity,
            Entity sourceEntity,
            SyncOptions options,
            Guid? creatorId,
            Guid? modifiedById,
            CT ct
         )
      {
         try
         {
            if (_syncStateService.IsSynced(preparedEntity.Id))
            {
               await _target.UpdateAsync(preparedEntity, ct, modifiedById);

               _logger.Information(
                  "Updated (Sync-Completion) {Key}:{Id}",
                  preparedEntity.LogicalName,
                  preparedEntity.Id
               );
            }
            else
            {
               await _target.CreateAsync(preparedEntity, ct, creatorId);

               _logger.Information(
                  "Created {Key}:{Id}",
                  preparedEntity.LogicalName,
                  preparedEntity.Id
               );

               if (modifiedById.HasValue && modifiedById != creatorId)
               {
                  var updateEntity = new Entity(
                     preparedEntity.LogicalName,
                     preparedEntity.Id
                  );

                  await _target.UpdateAsync(updateEntity, ct, modifiedById);
               }
            }

            return (true, string.Empty);
         }
         catch (Exception ex)
         {
            return await HandleSyncExceptionAsync(
               ex,
               preparedEntity,
               sourceEntity,
               options,
               creatorId,
               modifiedById,
               ct
            );
         }
      }

      #endregion

      #region Failure Management

      /// <inheritdoc />
      public async Task LogFailureToTargetAsync(
         Entity entity,
         string errorMessage,
         CT ct = default
      )
      {
         try
         {
            var failure = new Entity(
               SystemConstants.MigrationFailure.EntityLogicalName
            );

            var failureName = EntityHelper.GetRecordKey(entity);

            failure[SystemConstants.MigrationFailure.Name] =
               failureName.Length <= 100
                  ? failureName
                  : failureName.Substring(0, 100);

            failure[SystemConstants.MigrationFailure.SourceId] =
               entity.Id.ToString();

            failure[SystemConstants.MigrationFailure.EntityLogicalNameAttr] =
               entity.LogicalName;

            failure[SystemConstants.MigrationFailure.ErrorMessage] =
               errorMessage;

            failure[SystemConstants.MigrationFailure.Timestamp] =
               DateTime.UtcNow;

            await _target.CreateAsync(failure, ct);
         }
         catch (Exception ex)
         {
            _logger.Error(
               ex,
               "Failed to log migration failure for {Entity}:{Id}",
               entity.LogicalName,
               entity.Id
            );
         }
      }

      /// <inheritdoc />
      public async Task<List<MigrationFailureRecord>> GetFailuresAsync(
         IDataverseProvider target,
         string? entityLogicalName = null,
         CT ct = default
      )
      {
         var query = new QueryExpression(
            SystemConstants.MigrationFailure.EntityLogicalName
         )
         {
            ColumnSet = new ColumnSet(
               SystemConstants.MigrationFailure.SourceId,
               SystemConstants.MigrationFailure.EntityLogicalNameAttr,
               SystemConstants.MigrationFailure.ErrorMessage,
               SystemConstants.MigrationFailure.Timestamp
            )
         };

         if (!string.IsNullOrEmpty(entityLogicalName))
            query.Criteria.AddCondition(
               SystemConstants.MigrationFailure.EntityLogicalNameAttr,
               ConditionOperator.Equal,
               entityLogicalName
            );

         query.AddOrder(
            SystemConstants.MigrationFailure.Timestamp,
            OrderType.Ascending
         );

         var result = await target.RetrieveMultipleAsync(query, ct);

         return result.Entities
            .Select(e => new MigrationFailureRecord
            {
               Id = e.Id,
               EntityLogicalName = e.GetAttributeValue<string>(
                  SystemConstants.MigrationFailure.EntityLogicalNameAttr
               ) ?? SystemConstants.MigrationFailure.NotAvailable,
               SourceId = e.GetAttributeValue<string>(
                  SystemConstants.MigrationFailure.SourceId
               ) ?? SystemConstants.MigrationFailure.NotAvailable,
               ErrorMessage = e.GetAttributeValue<string>(
                  SystemConstants.MigrationFailure.ErrorMessage
               ) ?? SystemConstants.MigrationFailure.NotAvailable,
               TimestampUtc = e.GetAttributeValue<DateTime>(
                  SystemConstants.MigrationFailure.Timestamp
               )
            })
            .ToList();
      }

      /// <inheritdoc />
      public async Task ClearFailuresAsync(
         IDataverseProvider target,
         CT ct = default
      )
      {
         var query = new QueryExpression(
            SystemConstants.MigrationFailure.EntityLogicalName
         )
         {
            ColumnSet = new ColumnSet(false)
         };

         var result = await target.RetrieveMultipleAsync(query, ct);

         foreach (var entity in result.Entities)
         {
            ct.ThrowIfCancellationRequested();
            await target.DeleteAsync(
               SystemConstants.MigrationFailure.EntityLogicalName,
               entity.Id,
               ct
            );
         }
      }

      /// <inheritdoc />
      public async Task<bool> IsFailureLoggingInitializedAsync(
         IDataverseProvider target,
         CT ct = default
      )
      {
         var meta = await target.GetEntityMetadataAsync(
            SystemConstants.MigrationFailure.EntityLogicalName,
            ct
         );

         return meta != null;
      }

      #endregion

      #region Audit Data Preservation (Internal)

      private async Task CreateSourceDataRecordAsync(
         IDataverseProvider target,
         Entity sourceEntity,
         CT ct = default
      )
      {
         if (!await CheckSourceDataEntityExistsAsync(target, ct))
            return;

         bool hasAuditData =
            sourceEntity.Contains(DataverseAttributes.CreatedOn) ||
            sourceEntity.Contains(DataverseAttributes.ModifiedOn);

         if (!hasAuditData)
            return;

         var sourceData = new Entity(
            SystemConstants.SourceData.EntityLogicalName
         );

         sourceData[SystemConstants.SourceData.EntityId] =
            sourceEntity.Id.ToString();

         sourceData[SystemConstants.SourceData.EntityLogicalNameAttr] =
            sourceEntity.LogicalName.ToLowerInvariant();

         if (sourceEntity.Contains(DataverseAttributes.CreatedOn))
            sourceData[SystemConstants.SourceData.CreatedOn] =
               sourceEntity[DataverseAttributes.CreatedOn];

         if (sourceEntity.Contains(DataverseAttributes.ModifiedOn))
            sourceData[SystemConstants.SourceData.ModifiedOn] =
               sourceEntity[DataverseAttributes.ModifiedOn];

         try
         {
            await target.CreateAsync(sourceData, ct);
         }
         catch (Exception ex)
         {
            _logger.Warning(
               ex,
               "Failed to create source data record for {Entity}:{Id}",
               sourceEntity.LogicalName,
               sourceEntity.Id
            );
         }
      }

      private async Task DeleteSourceDataRecordAsync(
         IDataverseProvider target,
         string logicalName,
         Guid entityId,
         CT ct = default
      )
      {
         if (!await CheckSourceDataEntityExistsAsync(target, ct))
            return;

         try
         {
            var entityName = SystemConstants.SourceData.EntityLogicalName;
            var primaryId = SystemConstants.SourceData.PrimaryId;
            var sourceEntityId = SystemConstants.SourceData.EntityId;
            var logicalNameAttr =
               SystemConstants.SourceData.EntityLogicalNameAttr;

            var fetchXml = $@"
<fetch version='1.0' output-format='xml-platform' mapping='logical' 
       distinct='false' count='1'>
  <entity name='{entityName}'>
    <attribute name='{primaryId}' />
    <filter type='and'>
      <condition attribute='{sourceEntityId}' operator='eq' 
                 value='{entityId}' />
      <condition attribute='{logicalNameAttr}' operator='eq' 
                 value='{logicalName.ToLowerInvariant()}' />
    </filter>
  </entity>
</fetch>";

            var result = await target.RetrieveMultipleAsync(
               new FetchExpression(fetchXml),
               ct
            );

            if (result.Entities.Any())
               await target.DeleteAsync(
                  SystemConstants.SourceData.EntityLogicalName,
                  result.Entities[0].Id,
                  ct
               );
         }
         catch (Exception ex)
         {
            _logger.Warning(
               ex,
               "Failed to delete source data record for {Entity}:{Id}",
               logicalName,
               entityId
            );
         }
      }

      private async Task<bool> CheckSourceDataEntityExistsAsync(
         IDataverseProvider target,
         CT ct
      )
      {
         if (_isSourceDataSupported.HasValue)
            return _isSourceDataSupported.Value;

         try
         {
            var meta = await target.GetEntityMetadataAsync(
               SystemConstants.SourceData.EntityLogicalName,
               ct
            );

            _isSourceDataSupported = meta != null;
         }
         catch
         {
            _isSourceDataSupported = false;
         }

         if (_isSourceDataSupported == false)
            _logger.Warning(
               "Source data preservation entity '{Entity}' not found. " +
               "Audit data preservation will be disabled.",
               SystemConstants.SourceData.EntityLogicalName
            );

         return _isSourceDataSupported.Value;
      }

      #endregion

      #region Private Helpers

      /// <inheritdoc />
      public async Task<Guid?> FindExistingOnTargetAsync(
         Entity entity,
         CT ct
      )
      {
         return await _entityService.FindExistingOnTargetAsync(
            entity,
            _target,
            ct
         );
      }

      private string FormatFailureMessage(string context, Exception ex)
      {
         var baseEx = ex.GetBaseException();

         return $"[{context}] {baseEx.Message}";
      }

      #endregion
   }
}


// --- Source: dvmig.Core\Synchronization\SyncOptions.cs ---
namespace dvmig.Core.Synchronization
{
   /// <summary>
   /// Contains configuration options that dictate the behavior of the
   /// synchronization process.
   /// </summary>
   public class SyncOptions
   {
      /// <summary>
      /// Gets or sets a value indicating whether to preserve original
      /// source audit data (timestamps and user attribution).
      /// </summary>
      public bool PreserveAuditData { get; set; } = true;

      /// <summary>
      /// Gets or sets a value indicating whether to automatically strip
      /// unresolvable lookups and retry the sync.
      /// </summary>
      public bool StripMissingDependencies { get; set; } = false;

      /// <summary>
      /// Gets or sets the maximum number of concurrent requests to the
      /// target environment.
      /// </summary>
      public int MaxDegreeOfParallelism { get; set; } = 10;

      /// <summary>
      /// Gets or sets a value indicating whether to force a re-sync of
      /// all records, bypassing the local state check.
      /// </summary>
      public bool ForceResync { get; set; } = false;

      /// <summary>
      /// Creates a copy of the current options with ForceResync set to true.
      /// </summary>
      public SyncOptions CloneWithForceResync()
      {
         return new SyncOptions
         {
            PreserveAuditData = PreserveAuditData,
            StripMissingDependencies = StripMissingDependencies,
            MaxDegreeOfParallelism = MaxDegreeOfParallelism,
            ForceResync = true
         };
      }
   }
}


// --- Source: dvmig.Core\Synchronization\SyncStateService.cs ---

namespace dvmig.Core.Synchronization
{
   /// <summary>
   /// Implementation of <see cref="ISyncStateService"/> that manages 
   /// in-memory state during the synchronization process.
   /// </summary>
   public class SyncStateService : ISyncStateService
   {
      private readonly ConcurrentDictionary<string, int> _recursionTracker =
         new ConcurrentDictionary<string, int>();

      private ConcurrentDictionary<Guid, byte> _syncedIds =
         new ConcurrentDictionary<Guid, byte>();

      /// <inheritdoc />
      public ConcurrentDictionary<string, HashSet<string>> TriedDependencies
      {
         get;
      } = new ConcurrentDictionary<string, HashSet<string>>();

      /// <inheritdoc />
      public ConcurrentDictionary<string, Guid> IdMappingCache
      {
         get;
      } = new ConcurrentDictionary<string, Guid>();

      /// <inheritdoc />
      public void InitializeSyncedIds(IEnumerable<Guid> ids)
      {
         _syncedIds = new ConcurrentDictionary<Guid, byte>(
            ids.Select(id => new KeyValuePair<Guid, byte>(id, 1))
         );
      }

      /// <inheritdoc />
      public bool IsSynced(Guid id)
      {
         return _syncedIds.ContainsKey(id);
      }

      /// <inheritdoc />
      public void MarkAsSynced(Guid id)
      {
         _syncedIds.TryAdd(id, 1);
      }

      /// <inheritdoc />
      public bool TryEnterRecordScope(string recordKey, int maxDepth)
      {
         var depth = _recursionTracker.AddOrUpdate(
            recordKey,
            1,
            (_, v) => v + 1
         );

         return depth <= maxDepth;
      }

      /// <inheritdoc />
      public void LeaveRecordScope(string recordKey)
      {
         _recursionTracker.AddOrUpdate(recordKey, 0, (_, v) => v - 1);
      }

      /// <inheritdoc />
      public void ClearState()
      {
         TriedDependencies.Clear();
         IdMappingCache.Clear();
         _recursionTracker.Clear();
      }
   }
}


// --- Source: dvmig.Core\Synchronization\UserService.cs ---

namespace dvmig.Core.Synchronization
{
   /// <summary>
   /// Resolves user references from a source Dataverse environment to a 
   /// target environment.
   /// </summary>
   public class UserService : IUserService
   {
      private readonly IDataverseProvider? _source;
      private readonly IDataverseProvider? _target;
      private readonly ILogger _logger;

      private readonly ConcurrentDictionary<Guid, EntityReference>
         _mappingCache = new ConcurrentDictionary<Guid, EntityReference>();

      private readonly ConcurrentDictionary<Guid, UserMappingSummary>
         _summaries = new ConcurrentDictionary<Guid, UserMappingSummary>();

      /// <summary>
      /// Initializes a new instance of the <see cref="UserService"/> class.
      /// </summary>
      /// <param name="logger">The logger instance.</param>
      /// <param name="source">Optional source Dataverse provider.</param>
      /// <param name="target">Optional target Dataverse provider.</param>
      public UserService(
         ILogger logger,
         IDataverseProvider? source = null,
         IDataverseProvider? target = null
      )
      {
         _logger = logger;
         _source = source;
         _target = target;
      }

      /// <inheritdoc />
      public void AddManualMapping(Guid sourceUserId, Guid targetUserId)
      {
         _mappingCache[sourceUserId] = new EntityReference(
            DataverseEntities.SystemUser.Name,
            targetUserId
         );

         _summaries[sourceUserId] = new UserMappingSummary(
            sourceUserId.ToString(),
            sourceUserId,
            targetUserId.ToString(),
            targetUserId,
            "Manual"
         );
      }

      /// <inheritdoc />
      public async Task<List<Guid>> GetRealActiveUsersAsync(
         IDataverseProvider provider,
         CancellationToken ct = default
      )
      {
         try
         {
            var query = new QueryExpression(DataverseEntities.SystemUser.Name)
            {
               ColumnSet = new ColumnSet(
                  DataverseAttributes.SystemUserId,
                  DataverseAttributes.FullName
               )
            };

            query.Criteria.AddCondition(
               DataverseAttributes.IsDisabled,
               ConditionOperator.Equal,
               false
            );

            // accessmode = 0 (Read-Write/Human)
            query.Criteria.AddCondition(
               DataverseAttributes.AccessMode,
               ConditionOperator.Equal,
               0
            );

            var results = await provider.RetrieveMultipleAsync(query, ct);

            var users = results.Entities
               .Select(e => new
               {
                  Id = e.Id,
                  Name = e.GetAttributeValue<string>(
                     DataverseAttributes.FullName
                  ) ?? "Unknown"
               })
               .ToList();

            if (users.Count > 0)
            {
               var names = string.Join(", ", users.Select(u => u.Name));

               _logger.Information(
                  $"Found {users.Count} users for randomization: {names}"
               );
            }
            else
            {
               _logger.Warning(
                  "No active Read-Write users found for randomization. " +
                  "Creations will use the default caller."
               );
            }

            return users.Select(u => u.Id).ToList();
         }
         catch (Exception ex)
         {
            _logger.Warning(
               ex,
               "Failed to retrieve available users for randomization. " +
               "Defaulting to execution user."
            );

            return new List<Guid>();
         }
      }

      /// <inheritdoc />
      public void ClearCache()
      {
         _mappingCache.Clear();
         _summaries.Clear();
      }

      /// <inheritdoc />
      public async Task MapAllSourceUsersAsync(CancellationToken ct = default)
      {
         if (_source == null)
         {
            throw new InvalidOperationException(
               "Source provider must be set to map all source users."
            );
         }

         _logger.Information("Mapping users...".t());

         var query = new QueryExpression(DataverseEntities.SystemUser.Name)
         {
            ColumnSet = new ColumnSet(
               DataverseAttributes.InternalEmailAddress,
               DataverseAttributes.DomainName,
               DataverseAttributes.FullName,
               DataverseAttributes.SystemUserId,
               DataverseAttributes.AccessMode,
               DataverseAttributes.FirstName
            )
         };

         query.Criteria.AddCondition(
            DataverseAttributes.IsDisabled,
            ConditionOperator.Equal,
            false
         );

         var results = await _source.RetrieveMultipleAsync(query, ct);

         foreach (var user in results.Entities)
            await MapUserInternalAsync(user, ct);
      }

      /// <inheritdoc />
      public Task<List<UserMappingSummary>> GetMappingSummaryAsync(
         CancellationToken ct = default
      )
      {
         return Task.FromResult(
            _summaries.Values.OrderBy(s => s.SourceName).ToList()
         );
      }

      /// <inheritdoc />
      public async Task<EntityReference?> MapUserAsync(
         EntityReference? sourceUser,
         CancellationToken ct = default
      )
      {
         if (sourceUser == null)
            return null;

         if (_mappingCache.TryGetValue(sourceUser.Id, out var targetRef))
            return targetRef;

         if (_source == null)
         {
            throw new InvalidOperationException(
               "Source provider must be set to map a user."
            );
         }

         _logger.Debug("Attempting to map source user {Id}", sourceUser.Id);

         var sourceUserData = await _source.RetrieveAsync(
            DataverseEntities.SystemUser.Name,
            sourceUser.Id,
            new[]
            {
               DataverseAttributes.InternalEmailAddress,
               DataverseAttributes.DomainName,
               DataverseAttributes.FullName,
               DataverseAttributes.AccessMode,
               DataverseAttributes.FirstName
            },
            ct
         );

         if (sourceUserData == null)
         {
            _logger.Warning("Source user {Id} not found.", sourceUser.Id);

            return null;
         }

         return await MapUserInternalAsync(sourceUserData, ct);
      }

      private async Task<EntityReference?> MapUserInternalAsync(
         Entity sourceUserData,
         CancellationToken ct
      )
      {
         var sourceUserId = sourceUserData.Id;

         if (_mappingCache.TryGetValue(sourceUserId, out var cached))
            return cached;

         var sourceFullName = sourceUserData.GetAttributeValue<string>(
            DataverseAttributes.FullName
         ) ?? "Unknown Source User";

         var accessMode = sourceUserData.GetAttributeValue<OptionSetValue>(
            DataverseAttributes.AccessMode
         );

         bool isHuman = accessMode?.Value == 0 &&
            !sourceFullName.StartsWith("#");

         var email = sourceUserData.GetAttributeValue<string>(
            DataverseAttributes.InternalEmailAddress
         );

         if (!string.IsNullOrEmpty(email))
         {
            _logger.Debug(
               "Searching for target user by email: {Email}",
               email
            );

            var mapped = await FindTargetUserAsync(
               DataverseAttributes.InternalEmailAddress,
               email,
               ct
            );

            if (mapped.HasValue)
            {
               _logger.Debug(
                  "Found match by email for user {Id}",
                  sourceUserId
               );

               _mappingCache[sourceUserId] = mapped.Value.UserRef;

               _summaries[sourceUserId] = new UserMappingSummary(
                  sourceFullName,
                  sourceUserId,
                  mapped.Value.FullName,
                  mapped.Value.UserRef.Id,
                  "Mapped",
                  isHuman
               );

               return mapped.Value.UserRef;
            }
         }

         var domainName = sourceUserData.GetAttributeValue<string>(
            DataverseAttributes.DomainName
         );

         if (!string.IsNullOrEmpty(domainName))
         {
            _logger.Debug(
               "Searching for target user by domain name: {Domain}",
               domainName
            );

            var mapped = await FindTargetUserAsync(
               DataverseAttributes.DomainName,
               domainName,
               ct
            );

            if (mapped.HasValue)
            {
               _logger.Debug(
                  "Found match by domain name for user {Id}",
                  sourceUserId
               );

               _mappingCache[sourceUserId] = mapped.Value.UserRef;

               _summaries[sourceUserId] = new UserMappingSummary(
                  sourceFullName,
                  sourceUserId,
                  mapped.Value.FullName,
                  mapped.Value.UserRef.Id,
                  "Mapped",
                  isHuman
               );

               return mapped.Value.UserRef;
            }
         }

         if (isHuman)
         {
            _logger.Warning(
               "Could not map source user {FullName} ({Id})",
               sourceFullName,
               sourceUserId
            );
         }

         _summaries[sourceUserId] = new UserMappingSummary(
            sourceFullName,
            sourceUserId,
            "Caller (Default)",
            Guid.Empty,
            "Unmapped",
            isHuman
         );

         return null;
      }

      private async Task<(EntityReference UserRef, string FullName)?>
         FindTargetUserAsync(
            string attribute,
            string value,
            CancellationToken ct
         )
      {
         if (_target == null)
         {
            throw new InvalidOperationException(
               "Target provider must be set to find target users."
            );
         }

         var query = new QueryByAttribute(DataverseEntities.SystemUser.Name)
         {
            ColumnSet = new ColumnSet(
               DataverseAttributes.SystemUserId,
               DataverseAttributes.FullName
            )
         };

         query.AddAttributeValue(attribute, value);

         var results = await _target.RetrieveMultipleAsync(query, ct);
         var user = results.Entities.FirstOrDefault();

         if (user != null)
         {
            var fullName = user.GetAttributeValue<string>(
               DataverseAttributes.FullName
            ) ?? "Unknown Target User";

            return (user.ToEntityReference(), fullName);
         }

         return null;
      }
   }
}
