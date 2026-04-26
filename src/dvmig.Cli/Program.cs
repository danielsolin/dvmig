using System.Runtime.Versioning;
using dvmig.Core.DataPreservation;
using dvmig.Core.Logging;
using dvmig.Core.Metadata;
using dvmig.Core.Provisioning;
using dvmig.Core.Seeding;
using dvmig.Core.Settings;
using dvmig.Core.Synchronization;
using dvmig.Providers;
using Microsoft.Crm.Sdk.Messages;
using Serilog;
using Spectre.Console;

namespace dvmig.Cli
{
    /// <summary>
    /// Entry point for the Terminal User Interface (TUI) version of the 
    /// Dataverse Migrator.
    /// </summary>
    [SupportedOSPlatform("windows")]
    class Program
    {
        private static IDataverseProvider? _source;
        private static IDataverseProvider? _target;
        private static ISyncEngine? _engine;
        private static ISetupService? _setupService;
        private static ITestDataSeeder? _seeder;
        private static IMetadataService? _metadataService;
        private static IReconciliationService? _reconciliationService;
        private static ISyncStateTracker? _stateTracker;
        private static ISettingsService? _settingsService;
        private static ILogger? _logger;

        private enum ConnectionDirection
        {
            Source,
            Target
        }

        /// <summary>
        /// Main application loop for the CLI. Handles connection, entity 
        /// selection, and migration execution.
        /// </summary>
        /// <param name="args">Command line arguments.</param>
        static async Task Main(string[] args)
        {
            _logger = LoggerInitializer.Initialize("dvmig.Cli");
            _settingsService = new SettingsService();
            _stateTracker = new LocalFileStateTracker();
            _seeder = new TestDataSeeder(_logger);
            _metadataService = new MetadataService();
            _reconciliationService = new ReconciliationService();

            bool enableSourceCleanup = args.Contains("--enable-source-cleanup");
            CliUI.WriteHeader();

            bool exit = false;
            while (!exit)
            {
                var choices = GetMainMenuChoices(enableSourceCleanup);
                var choice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("What would you like to do?")
                        .PageSize(10)
                        .AddChoices(choices));

                switch (choice)
                {
                    case "Migrate Data":
                        await HandleMigrationAsync();
                        break;
                    case "Reconcile Migration / View Failures":
                        await HandleReconcileAsync();
                        break;
                    case "Seed Test Data":
                        await HandleSeedingAsync();
                        break;
                    case "Install/Update dvmig Components on Target":
                        await HandleInstallAsync();
                        break;
                    case "Uninstall dvmig Components from Target":
                        await HandleTargetComponentsCleanupAsync();
                        break;
                    case "Wipe ALL Accounts, Contacts, and Activities from " +
                         "Source (DANGEROUS)":
                        await HandleSourceTestDataCleanupAsync();
                        break;
                    case "Exit":
                        exit = true;
                        break;
                }

                if (!exit)
                {
                    CliUI.Pause();
                    CliUI.WriteHeader();
                }
            }
        }

        private static List<string> GetMainMenuChoices(bool enableSourceCleanup)
        {
            var choices = new List<string>
            {
                "Migrate Data",
                "Reconcile Migration / View Failures",
                "Seed Test Data",
                "Install/Update dvmig Components on Target",
                "Uninstall dvmig Components from Target"
            };

            if (enableSourceCleanup)
            {
                choices.Add(
                    "Wipe ALL Accounts, Contacts, and Activities from " +
                    "Source (DANGEROUS)"
                );
            }

            choices.Add("Exit");

            return choices;
        }

        private static async Task HandleMigrationAsync()
        {
            _source = await ConnectAsync(ConnectionDirection.Source);
            if (_source == null)
            {
                return;
            }

            _target = await ConnectAsync(ConnectionDirection.Target);
            if (_target == null)
            {
                return;
            }

            _setupService ??= CreateSetupService();

            bool isReady = await _setupService.IsEnvironmentReadyAsync(
                _target,
                default
            );

            if (!isReady)
            {
                var prepareMsg = "[yellow]Target environment is not " +
                                 "prepared for migration. Prepare it now?[/]";

                if (AnsiConsole.Confirm(prepareMsg, true))
                {
                    await HandleInstallAsync(_target);
                }
                else
                {
                    CliUI.WriteError(
                        "Migration cannot proceed without components."
                    );

                    return;
                }
            }

            var userMapper = new UserMapper(_source, _target, _logger!);
            var dataPreservation = new DataPreservationManager(
                _target,
                _logger!
            );

            _engine = new SyncEngine(
                _source,
                _target,
                userMapper,
                dataPreservation,
                _stateTracker!,
                _logger!
            );

            var selectedEntities = await SelectEntitiesAsync();
            if (selectedEntities == null || selectedEntities.Count == 0)
            {
                CliUI.WriteWarning("No entities selected.");

                return;
            }

            await RunMigrationAsync(selectedEntities);
            CliUI.WriteSuccess("Migration Finished!");
        }

        private static async Task HandleReconcileAsync()
        {
            if (_target == null)
            {
                _target = await ConnectAsync(ConnectionDirection.Target);
            }

            if (_target == null)
            {
                return;
            }

            bool isInitialized =
                await _reconciliationService!.IsInitializedAsync(
                    _target,
                    default
                );

            if (!isInitialized)
            {
                CliUI.WriteWarning(
                    "Migration failure logging is not initialized on " +
                    "this target."
                );
                AnsiConsole.MarkupLine(
                    "[grey]Please use 'Install/Update dvmig Components' to " +
                    "enable this feature.[/]"
                );

                return;
            }

            var failures = await CliUI.RunStatusAsync(
                "Fetching recorded migration failures...",
                async () => await _reconciliationService.GetFailuresAsync(
                    _target,
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
                await CliUI.RunStatusAsync("Clearing failure log...", async _ =>
                {
                    await _reconciliationService.ClearFailuresAsync(
                        _target,
                        default
                    );
                });

                CliUI.WriteSuccess("Failure log cleared.");
            }
        }

        private static async Task HandleSeedingAsync()
        {
            var provider = _source ?? await ConnectAsync(
                ConnectionDirection.Source
            );
            if (provider == null)
                return;

            var prompt = "How many [bold blue]Accounts[/] would you like " +
                         "to generate?";

            int count = AnsiConsole.Ask<int>(prompt, 100);

            await CliUI.RunStatusAsync(
                "Seeding data...",
                async progress => await _seeder!.SeedTestDataAsync(
                    provider,
                    count,
                    progress
                )
            );

            CliUI.WriteSuccess("Seeding Finished!");
        }

        private static async Task HandleInstallAsync(
            IDataverseProvider? target = null
        )
        {
            var provider = target ?? _target;
            if (provider == null)
            {
                provider = _target = await ConnectAsync(
                    ConnectionDirection.Target
                );
            }

            if (provider == null)
            {
                return;
            }

            _setupService ??= CreateSetupService();

            await CliUI.RunStatusAsync(
                "Installing components...",
                async progress =>
                {
                    await _setupService.CreateSchemaAsync(provider, progress);
                    await _setupService.DeployPluginAsync(provider, progress);
                }
            );

            CliUI.WriteSuccess("Installation Finished!");
        }

        private static ISetupService CreateSetupService()
        {
            return new SetupService(
                new EnvironmentValidator(),
                new SchemaManager(_logger!),
                new PluginDeployer(_logger!),
                _logger!
            );
        }

        private static async Task HandleTargetComponentsCleanupAsync()
        {
            var provider = _target ?? await ConnectAsync(
                ConnectionDirection.Target
            );

            if (provider == null)
            {
                return;
            }

            var promptMsg = "[red]Are you sure you want to remove all dvmig " +
                            "system components (schema and plugins) from " +
                            "this environment?[/]";

            if (!AnsiConsole.Confirm(promptMsg, false))
            {
                return;
            }

            _setupService ??= CreateSetupService();

            await CliUI.RunStatusAsync(
                "Uninstalling components...",
                async progress => await _setupService.CleanEnvironmentAsync(
                    provider,
                    progress
                )
            );

            CliUI.WriteSuccess("Uninstallation Finished!");
        }

        private static async Task HandleSourceTestDataCleanupAsync()
        {
            var provider = _source ?? await ConnectAsync(
                ConnectionDirection.Source
            );
            if (provider == null)
            {
                return;
            }

            AnsiConsole.MarkupLine(
                "[bold red]CRITICAL WARNING:[/] This operation will delete " +
                "[bold]EVERY SINGLE[/] Account, Contact, Task, Phone Call, " +
                "and Email record from the selected environment."
            );
            AnsiConsole.MarkupLine(
                "[red]This is NOT restricted to test data. Real data will " +
                "be destroyed.[/]"
            );
            AnsiConsole.MarkupLine(
                "[red]This action is permanent and irreversible.[/]"
            );

            var confirmation = AnsiConsole.Ask<string>(
                "Type [bold red]WIPE ALL DATA[/] to confirm:"
            );

            if (confirmation != "WIPE ALL DATA")
            {
                CliUI.WriteWarning("Wipe cancelled.");

                return;
            }

            await CliUI.RunStatusAsync(
                "Wiping data...",
                async progress => await _seeder!.CleanTestDataAsync(
                    provider,
                    progress
                )
            );

            CliUI.WriteSuccess("Data Wipe Finished!");
        }

        private static async Task<IDataverseProvider?> ConnectAsync(
            ConnectionDirection direction,
            string? label = null
        )
        {
            label ??= direction.ToString();

            var settings = _settingsService!.LoadSettings();
            string? storedConn = direction == ConnectionDirection.Source
                ? settings.SourceConnectionString
                : settings.TargetConnectionString;

            string connStr;
            if (!string.IsNullOrEmpty(storedConn))
            {
                var preview = ConnectionHelper.MaskConnectionString(storedConn);

                var useStored = AnsiConsole.Confirm(
                    $"Use [green]stored[/] {label} connection string?\n" +
                    $"[grey]({preview})[/]",
                    true
                );

                connStr = useStored ? storedConn : AnsiConsole.Ask<string>(
                    $"Enter [bold blue]{label}[/] Connection String:"
                );
            }
            else
            {
                connStr = AnsiConsole.Ask<string>(
                    $"Enter [bold blue]{label}[/] Connection String:"
                );
            }

            var isLegacy = AnsiConsole.Confirm(
                $"Is [bold blue]{label}[/] Legacy CRM (OnPrem)?",
                false
            );

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
                            $"[red]×[/] Failed to connect to {label}: " +
                            $"{ex.Message}"
                        );

                        return null;
                    }
                }
            );

            if (provider != null)
            {
                CliUI.WriteSuccess($"Connected to {label}");

                if (connStr != storedConn)
                {
                    var savePrompt = $"Save this {label} connection string " +
                                     "for future use?";

                    if (AnsiConsole.Confirm(savePrompt, true))
                    {
                        settings.RememberConnections = true;
                        if (direction == ConnectionDirection.Source)
                        {
                            settings.SourceConnectionString = connStr;
                        }
                        else
                        {
                            settings.TargetConnectionString = connStr;
                        }

                        _settingsService.SaveSettings(settings);
                        AnsiConsole.MarkupLine("[grey]Settings saved.[/]");
                    }
                }
            }

            return provider;
        }

        private static async Task<List<string>?> SelectEntitiesAsync()
        {
            var entities = await CliUI.RunStatusAsync(
                "Fetching entity metadata...",
                async () =>
                {
                    try
                    {
                        return await _metadataService!
                            .GetMigrationEntitiesAsync(
                                _source!,
                                default
                            );
                    }
                    catch (Exception ex)
                    {
                        CliUI.WriteError(
                            $"Failed to fetch metadata: {ex.Message}"
                        );

                        return null;
                    }
                }
            );

            if (entities == null || entities.Count == 0)
            {
                return null;
            }

            var prompt = new MultiSelectionPrompt<string>()
                .Title("Select [green]Entities[/] to migrate:")
                .PageSize(15)
                .MoreChoicesText("[grey](Move up and down to reveal more)[/]")
                .InstructionsText(
                    "[grey](Press [blue]<space>[/] to toggle, " +
                    "[green]<enter>[/] to accept)[/]"
                );

            foreach (var entity in entities)
            {
                prompt.AddChoice(entity.LogicalName);
            }

            return AnsiConsole.Prompt(prompt);
        }

        private static async Task RunMigrationAsync(List<string> entities)
        {
            foreach (var logicalName in entities)
            {
                AnsiConsole.MarkupLine(
                    $"[bold yellow]Migrating {logicalName}...[/]"
                );

                await _engine!.InitializeEntitySyncAsync(logicalName);

                int processed = 0;
                if (_stateTracker!.StateExists())
                {
                    var syncedIds = await _stateTracker.GetSyncedIdsAsync();
                    if (syncedIds.Count > 0)
                    {
                        var resumeMsg = $"Previous migration state found " +
                                        $"for {logicalName} " +
                                        $"({syncedIds.Count} " +
                                        "records already synced). Resume " +
                                        "(y) or start over (n) ?";

                        if (!AnsiConsole.Confirm(resumeMsg, true))
                        {
                            await _stateTracker.ClearStateAsync();
                            await _engine.InitializeEntitySyncAsync(
                                logicalName
                            );
                        }
                        else
                        {
                            processed = syncedIds.Count;
                        }
                    }
                }

                long totalCount = await _metadataService!.GetRecordCountAsync(
                    _source!,
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
                                       $"({processed}/{totalCount})";

                        var task = ctx.AddTask(taskName, true, totalCount);
                        task.Value = processed;

                        var recordProgress = new Progress<bool>(success =>
                        {
                            processed++;
                            task.Value = processed;
                            task.Description = $"Syncing {logicalName} " +
                                               $"({processed}/{totalCount})";
                        });

                        var options = new SyncOptions
                        {
                            StripMissingDependencies = true
                        };

                        await _engine.SyncEntityAsync(
                            logicalName,
                            options,
                            null,
                            null,
                            recordProgress,
                            default
                        );
                    });
            }
        }
    }
}
