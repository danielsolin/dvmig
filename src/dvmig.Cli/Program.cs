using dvmig.Core.DataPreservation;
using dvmig.Core.Logging;
using dvmig.Core.Metadata;
using dvmig.Core.Provisioning;
using dvmig.Core.Seeding;
using dvmig.Core.Settings;
using dvmig.Core.Synchronization;
using dvmig.Providers;
using dvmig.Shared.Metadata;
using Microsoft.Crm.Sdk.Messages;
using System.Runtime.Versioning;
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
        private static ISyncStateTracker? _stateTracker;
        private static ISettingsService? _settingsService;
        private static ILogger? _logger;

        /// <summary>
        /// Main application loop for the CLI. Handles connection, entity 
        /// selection, and migration execution.
        /// </summary>
        /// <param name="args">Command line arguments.</param>
        static async Task Main(string[] args)
        {
            AnsiConsole.Clear();
            _logger = LoggerInitializer.Initialize("dvmig.Cli");

            _settingsService = new SettingsService();
            _stateTracker = new LocalFileStateTracker();
            _seeder = new TestDataSeeder(_logger);
            _metadataService = new MetadataService();

            bool enableSourceCleanup = args.Contains("--enable-source-cleanup");

            AnsiConsole.Write(
                new FigletText("DVMIG")
                    .Color(Color.Blue)
            );

            AnsiConsole.MarkupLine(
                "[bold]Dataverse Migrator - Technical UI[/]"
            );
            AnsiConsole.WriteLine();

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
                        await HandleCleanupAsync();
                        break;
                    case "Wipe ALL Accounts, Contacts, and Activities from " +
                         "Source (DANGEROUS)":
                        await HandleSourceCleanupAsync();
                        break;
                    case "Exit":
                        exit = true;
                        break;
                }

                if (!exit)
                {
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine(
                        "[grey]Press any key to return to menu...[/]"
                    );
                    Console.ReadKey(true);
                    AnsiConsole.Clear();
                    
                    AnsiConsole.Write(
                        new FigletText("DVMIG")
                            .Color(Color.Blue)
                    );
                }
            }
        }

        private static List<string> GetMainMenuChoices(bool enableSourceCleanup)
        {
            var choices = new List<string> {
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
            _source = await ConnectAsync("Source");
            if (_source == null)
            {
                return;
            }

            _target = await ConnectAsync("Target");
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
                    AnsiConsole.MarkupLine(
                        "[red]Migration cannot proceed without components.[/]"
                    );

                    return;
                }
            }

            var userMapper = new UserMapper(_source, _target, _logger!);
            var dataPreservation = new DataPreservationManager(_target, _logger!);
            
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
                AnsiConsole.MarkupLine("[yellow]No entities selected.[/]");

                return;
            }

            await RunMigrationAsync(selectedEntities);
            AnsiConsole.MarkupLine("[bold green]Migration Finished![/]");
        }

        private static async Task HandleReconcileAsync()
        {
            _target = await ConnectAsync("Target");
            if (_target == null)
            {
                return;
            }

            var meta = await _target.GetEntityMetadataAsync(
                SchemaConstants.MigrationFailure.EntityLogicalName, 
                default
            );

            if (meta == null)
            {
                AnsiConsole.MarkupLine(
                    "[yellow]Migration failure logging is not initialized on " +
                    "this target.[/]"
                );
                AnsiConsole.MarkupLine(
                    "[grey]Please use 'Install/Update dvmig Components' to " +
                    "enable this feature.[/]"
                );

                return;
            }

            AnsiConsole.MarkupLine(
                "[bold blue]Fetching recorded migration failures...[/]"
            );

            var query = new Microsoft.Xrm.Sdk.Query.QueryExpression(
                SchemaConstants.MigrationFailure.EntityLogicalName
            )
            {
                ColumnSet = new Microsoft.Xrm.Sdk.Query.ColumnSet(
                    SchemaConstants.MigrationFailure.SourceId, 
                    SchemaConstants.MigrationFailure.EntityLogicalNameAttr, 
                    SchemaConstants.MigrationFailure.ErrorMessage, 
                    SchemaConstants.MigrationFailure.Timestamp
                )
            };
            query.AddOrder(
                SchemaConstants.MigrationFailure.Timestamp, 
                Microsoft.Xrm.Sdk.Query.OrderType.Descending
            );

            var failures = await _target.RetrieveMultipleAsync(query, default);

            if (failures.Entities.Count == 0)
            {
                AnsiConsole.MarkupLine(
                    "[green]No migration failures recorded in Target " +
                    "environment.[/]"
                );

                return;
            }

            var table = new Table();
            table.AddColumn("Entity");
            table.AddColumn("Source ID");
            table.AddColumn("Timestamp (UTC)");
            table.AddColumn("Error Message");

            foreach (var failure in failures.Entities)
            {
                table.AddRow(
                    failure.GetAttributeValue<string>(
                        SchemaConstants.MigrationFailure.EntityLogicalNameAttr
                    ) ?? "N/A",
                    failure.GetAttributeValue<string>(
                        SchemaConstants.MigrationFailure.SourceId
                    ) ?? "N/A",
                    failure.GetAttributeValue<DateTime>(
                        SchemaConstants.MigrationFailure.Timestamp
                    ).ToString("yyyy-MM-dd HH:mm:ss"),
                    failure.GetAttributeValue<string>(
                        SchemaConstants.MigrationFailure.ErrorMessage
                    ) ?? "N/A"
                );
            }

            AnsiConsole.Write(table);

            var clearLog = "Would you like to clear the failure log on " +
                           "the target?";

            if (AnsiConsole.Confirm(clearLog, false))
            {
                await AnsiConsole.Status()
                    .StartAsync("Clearing failure log...", async ctx =>
                    {
                        foreach (var failure in failures.Entities)
                        {
                            await _target.DeleteAsync(
                                SchemaConstants.MigrationFailure.EntityLogicalName, 
                                failure.Id, 
                                default
                            );
                        }
                    });

                AnsiConsole.MarkupLine("[green]Failure log cleared.[/]");
            }
        }

        private static async Task HandleSeedingAsync()
        {
            var provider = await ConnectAsync("Source Environment to Seed");
            if (provider == null)
            {
                return;
            }

            var prompt = "How many [bold blue]Accounts[/] would you like " +
                         "to generate?";

            int count = AnsiConsole.Ask<int>(prompt, 100);

            await AnsiConsole.Status()
                .StartAsync("Seeding data...", async ctx =>
                {
                    var progress = new Progress<string>(msg =>
                    {
                        AnsiConsole.MarkupLine(
                            $"[grey][[{DateTime.Now:HH:mm:ss}]][/] {msg}"
                        );
                    });

                    await _seeder!.SeedTestDataAsync(provider, count, progress);
                });

            AnsiConsole.MarkupLine("[bold green]Seeding Finished![/]");
        }

        private static async Task HandleInstallAsync(
            IDataverseProvider? target = null
        )
        {
            var provider = target ?? 
                           await ConnectAsync("Target Environment to Install on");

            if (provider == null)
            {
                return;
            }

            _setupService ??= CreateSetupService();

            await AnsiConsole.Status()
                .StartAsync("Installing components...", async ctx =>
                {
                    var progress = new Progress<string>(msg =>
                    {
                        AnsiConsole.MarkupLine(
                            $"[grey][[{DateTime.Now:HH:mm:ss}]][/] {msg}"
                        );
                    });

                    await _setupService.CreateSchemaAsync(provider, progress);

                    var assemblyPath = Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory,
                        "dvmig.Plugins.dll"
                    );

                    if (!File.Exists(assemblyPath))
                    {
                        assemblyPath = Path.Combine(
                            AppDomain.CurrentDomain.BaseDirectory,
                            "..", "..", "..", "..",
                            "dvmig.Plugins", "bin", "Debug", "netstandard2.0",
                            "dvmig.Plugins.dll"
                        );
                    }

                    await _setupService.DeployPluginAsync(
                        provider, 
                        assemblyPath, 
                        progress
                    );
                });

            AnsiConsole.MarkupLine("[bold green]Installation Finished![/]");
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

        private static async Task HandleCleanupAsync()
        {
            var provider = await ConnectAsync(
                "Target Environment to Uninstall from"
            );

            if (provider == null)
            {
                return;
            }

            var promptMsg = "[red]Are you sure you want to remove all dvmig " +
                            "system components (schema and plugins) from this " +
                            "environment?[/]";

            if (!AnsiConsole.Confirm(promptMsg, false))
            {
                return;
            }

            _setupService ??= CreateSetupService();

            await AnsiConsole.Status()
                .StartAsync("Uninstalling components...", async ctx =>
                {
                    var progress = new Progress<string>(msg =>
                    {
                        AnsiConsole.MarkupLine(
                            $"[grey][[{DateTime.Now:HH:mm:ss}]][/] {msg}"
                        );
                    });

                    await _setupService.CleanEnvironmentAsync(provider, progress);
                });

            AnsiConsole.MarkupLine("[bold green]Uninstallation Finished![/]");
        }

        private static async Task HandleSourceCleanupAsync()
        {
            var provider = await ConnectAsync("Source Environment to Wipe");
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
                AnsiConsole.MarkupLine("[yellow]Wipe cancelled.[/]");

                return;
            }

            await AnsiConsole.Status()
                .StartAsync("Wiping data...", async ctx =>
                {
                    var progress = new Progress<string>(msg =>
                    {
                        AnsiConsole.MarkupLine(
                            $"[grey][[{DateTime.Now:HH:mm:ss}]][/] {msg}"
                        );
                    });

                    await _seeder!.CleanTestDataAsync(provider, progress);
                });

            AnsiConsole.MarkupLine("[bold green]Data Wipe Finished![/]");
        }

        private static async Task<IDataverseProvider?> ConnectAsync(
            string label
        )
        {
            var settings = _settingsService!.LoadSettings();
            string? storedConn = label.Contains(
                "Source", 
                StringComparison.OrdinalIgnoreCase
            ) 
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

            IDataverseProvider? provider = await AnsiConsole.Status()
                .StartAsync($"Connecting to {label}...", async ctx =>
                {
                    try
                    {
                        IDataverseProvider p = isLegacy
                            ? new LegacyCrmProvider(connStr)
                            : new DataverseProvider(connStr);

                        await p.ExecuteAsync(new WhoAmIRequest(), default);
                        AnsiConsole.MarkupLine($"[green]✓[/] Connected to {label}");

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
                });

            if (provider != null && connStr != storedConn)
            {
                var savePrompt = $"Save this {label} connection string " +
                                 "for future use?";

                if (AnsiConsole.Confirm(savePrompt, true))
                {
                    settings.RememberConnections = true;
                    if (label.Contains(
                            "Source", 
                            StringComparison.OrdinalIgnoreCase))
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

            return provider;
        }

        private static async Task<List<string>?> SelectEntitiesAsync()
        {
            var entities = await AnsiConsole.Status()
                .StartAsync("Fetching entity metadata...", async ctx =>
                {
                    try
                    {
                        return await _metadataService!.GetMigrationEntitiesAsync(
                            _source!, 
                            default
                        );
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine(
                            $"[red]×[/] Failed to fetch metadata: {ex.Message}"
                        );

                        return null;
                    }
                });

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
                AnsiConsole.MarkupLine($"[bold yellow]Migrating {logicalName}...[/]");

                await _engine!.InitializeEntitySyncAsync(logicalName);

                if (_stateTracker!.StateExists())
                {
                    var syncedIds = await _stateTracker.GetSyncedIdsAsync();
                    if (syncedIds.Count > 0)
                    {
                        var resumeMsg = $"Previous migration state found " +
                                        $"for {logicalName} ({syncedIds.Count} " +
                                        "records already synced). Resume?";

                        if (!AnsiConsole.Confirm(resumeMsg, true))
                        {
                            await _stateTracker.ClearStateAsync();
                            await _engine.InitializeEntitySyncAsync(logicalName);
                        }
                    }
                }

                long totalCount = await _seeder!.GetRecordCountAsync(
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
                    .Columns(new ProgressColumn[] 
                    {
                        new TaskDescriptionColumn(),
                        new ProgressBarColumn(),
                        new PercentageColumn(),
                        new RemainingTimeColumn(),
                        new SpinnerColumn(),
                    })
                    .StartAsync(async ctx =>
                    {
                        var syncedIds = await _stateTracker.GetSyncedIdsAsync();
                        int processed = syncedIds.Count;

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

                        await _engine.SyncEntityAsync(
                            logicalName,
                            new SyncOptions { StripMissingDependencies = true },
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
