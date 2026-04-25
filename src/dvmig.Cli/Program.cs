using dvmig.Core.DataPreservation;
using dvmig.Core.Logging;
using dvmig.Core.Metadata;
using dvmig.Core.Provisioning;
using dvmig.Core.Seeding;
using dvmig.Core.Settings;
using dvmig.Core.Synchronization;
using dvmig.Providers;
using Microsoft.Xrm.Sdk;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Messages;
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
                    case "Wipe ALL Accounts, Contacts, and Activities from Source (DANGEROUS)":
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

        private static async Task HandleMigrationAsync()
        {
            // 1. Source Connection
            _source = await ConnectAsync("Source");
            if (_source == null)
            {
                return;
            }

            // 2. Target Connection
            _target = await ConnectAsync("Target");
            if (_target == null)
            {
                return;
            }

            // Check if target environment is ready
            _setupService ??= CreateSetupService();
            bool isReady = await _setupService.IsEnvironmentReadyAsync(
                _target, 
                default
            );

            if (!isReady)
            {
                var prepare = AnsiConsole.Confirm(
                    "[yellow]Target environment is not prepared for migration. " +
                    "Prepare it now?[/]", 
                    true
                );

                if (prepare)
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

            // 3. Initialize Services
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

            // 4. Entity Selection
            var selectedEntities = await SelectEntitiesAsync();
            if (selectedEntities == null || selectedEntities.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No entities selected.[/]");

                return;
            }

            // 5. Run Migration
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

            // Check if failure logging entity exists
            var meta = await _target.GetEntityMetadataAsync(
                "dm_migrationfailure", 
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
                "dm_migrationfailure"
            )
            {
                ColumnSet = new Microsoft.Xrm.Sdk.Query.ColumnSet(
                    "dm_sourceid", 
                    "dm_entitylogicalname", 
                    "dm_errormessage", 
                    "dm_timestamp"
                )
            };
            query.AddOrder(
                "dm_timestamp", 
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
                        "dm_entitylogicalname"
                    ) ?? "N/A",
                    failure.GetAttributeValue<string>("dm_sourceid") ?? "N/A",
                    failure.GetAttributeValue<DateTime>(
                        "dm_timestamp"
                    ).ToString("yyyy-MM-dd HH:mm:ss"),
                    failure.GetAttributeValue<string>("dm_errormessage") ?? "N/A"
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
                                "dm_migrationfailure", 
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

            int count = AnsiConsole.Ask<int>(
                "How many [bold blue]Accounts[/] would you like to generate?", 
                100
            );

            _seeder ??= new TestDataSeeder(_logger!);

            await AnsiConsole.Status()
                .StartAsync("Seeding data...", async ctx =>
                {
                    var progress = new Progress<string>(msg =>
                    {
                        AnsiConsole.MarkupLine(
                            $"[grey][[{DateTime.Now:HH:mm:ss}]][/] {msg}"
                        );
                    });

                    await _seeder.SeedTestDataAsync(provider, count, progress);
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

                    // 1. Create Schema
                    await _setupService.CreateSchemaAsync(provider, progress);

                    // 2. Deploy Plugin
                    var assemblyPath = Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory,
                        "dvmig.Plugins.dll"
                    );

                    // Fallback for development if not in same folder
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

            _seeder ??= new TestDataSeeder(_logger!);

            await AnsiConsole.Status()
                .StartAsync("Wiping data...", async ctx =>
                {
                    var progress = new Progress<string>(msg =>
                    {
                        AnsiConsole.MarkupLine(
                            $"[grey][[{DateTime.Now:HH:mm:ss}]][/] {msg}"
                        );
                    });

                    await _seeder.CleanTestDataAsync(provider, progress);
                });

            AnsiConsole.MarkupLine("[bold green]Data Wipe Finished![/]");
        }

        /// <summary>
        /// Interactively connects to a Dataverse/CRM environment.
        /// </summary>
        /// <param name="label">The label for the connection (Source/Target).</param>
        /// <returns>A configured Dataverse provider, or null if connection fails.</returns>
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
                // Mask sensitive info for preview (passwords/secrets)
                var preview = MaskConnectionString(storedConn);
                
                var useStored = AnsiConsole.Confirm(
                    $"Use [green]stored[/] {label} connection string?\n" +
                    $"[grey]({preview})[/]", 
                    true
                );

                if (useStored)
                {
                    connStr = storedConn;
                }
                else
                {
                    connStr = AnsiConsole.Ask<string>(
                        $"Enter [bold blue]{label}[/] Connection String:"
                    );
                }
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

                        // Test connection
                        await p.ExecuteAsync(
                            new WhoAmIRequest(),
                            default
                        );

                        AnsiConsole.MarkupLine(
                            $"[green]✓[/] Connected to {label}"
                        );

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

        /// <summary>
        /// Interactively allows the user to select entities for migration.
        /// </summary>
        /// <returns>A list of selected entity logical names.</returns>
        private static async Task<List<string>?> SelectEntitiesAsync()
        {
            List<Microsoft.Xrm.Sdk.Metadata.EntityMetadata>? entities = null;

            await AnsiConsole.Status()
                .StartAsync("Fetching entity metadata...", async ctx =>
                {
                    try
                    {
                        var request = new RetrieveAllEntitiesRequest
                        {
                            EntityFilters = Microsoft.Xrm.Sdk.Metadata
                                .EntityFilters.Entity,
                            RetrieveAsIfPublished = true
                        };

                        var response = (RetrieveAllEntitiesResponse)await
                            _source!.ExecuteAsync(request, default);

                        entities = response.EntityMetadata
                            .Where(e =>
                                (e.IsCustomEntity == true ||
                                 IsStandardEntity(e.LogicalName)) &&
                                e.IsIntersect == false &&
                                e.IsValidForAdvancedFind == true &&
                                !string.IsNullOrEmpty(
                                    e.DisplayName?.UserLocalizedLabel?.Label))
                            .OrderBy(e =>
                                e.DisplayName?.UserLocalizedLabel?.Label ??
                                e.LogicalName)
                            .ToList();
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine(
                            $"[red]×[/] Failed to fetch metadata: {ex.Message}"
                        );
                    }
                });

            if (entities == null || entities.Count == 0)
            {
                return null;
            }

            var prompt = new MultiSelectionPrompt<string>()
                .Title("Select [green]Entities[/] to migrate:")
                .PageSize(15)
                .MoreChoicesText(
                    "[grey](Move up and down to reveal more)[/]"
                )
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

        /// <summary>
        /// Checks if an entity is considered a standard (non-system) entity.
        /// </summary>
        private static bool IsStandardEntity(string logicalName)
        {
            return EntityMetadataHelper.IsStandardEntity(logicalName);
        }

        /// <summary>
        /// Executes the migration for the selected entities.
        /// </summary>
        /// <param name="entities">The list of logical names to migrate.</param>
        private static async Task RunMigrationAsync(List<string> entities)
        {
            foreach (var logicalName in entities)
            {
                AnsiConsole.MarkupLine(
                    $"[bold yellow]Migrating {logicalName}...[/]"
                );

                // 1. Initialize Engine for this entity
                await _engine!.InitializeEntitySyncAsync(logicalName);

                // 2. Resume Check
                if (_stateTracker!.StateExists())
                {
                    var syncedIds = await _stateTracker.GetSyncedIdsAsync();
                    if (syncedIds.Count > 0)
                    {
                        var resume = AnsiConsole.Confirm(
                            $"Previous migration state found for {logicalName} " +
                            $"({syncedIds.Count} records already synced). Resume?", 
                            true
                        );

                        if (!resume)
                        {
                            await _stateTracker.ClearStateAsync();
                            await _engine.InitializeEntitySyncAsync(logicalName);
                        }
                    }
                }

                // 3. Setup Query
                var columns = await _engine.GetValidColumnsAsync(
                    logicalName, 
                    default
                );

                var query = new Microsoft.Xrm.Sdk.Query.QueryExpression(
                    logicalName
                )
                {
                    ColumnSet = columns,
                    PageInfo = new Microsoft.Xrm.Sdk.Query.PagingInfo
                    {
                        Count = 500,
                        PageNumber = 1
                    }
                };

                // 4. Get Total Count for progress bar
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

                // 5. Paginated Sync Loop
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

                        var task = ctx.AddTask(
                            $"Syncing {logicalName} ({processed}/{totalCount})",
                            true,
                            totalCount
                        );
                        task.Value = processed;

                        var recordProgress = new Progress<bool>(success =>
                        {
                            processed++;
                            task.Value = processed;
                            task.Description = 
                                $"Syncing {logicalName} ({processed}/{totalCount})";
                        });

                        while (true)
                        {
                            var response = await _source!.RetrieveMultipleAsync(
                                query,
                                default
                            );

                            await _engine.SyncAsync(
                                response.Entities,
                                new SyncOptions { StripMissingDependencies = true },
                                null,
                                recordProgress,
                                default
                            );

                            if (!response.MoreRecords)
                            {
                                break;
                            }

                            query.PageInfo.PageNumber++;
                            query.PageInfo.PagingCookie = response.PagingCookie;
                        }
                    });
            }
        }

        private static string MaskConnectionString(string connectionString)
        {
            var parts = connectionString.Split(
                ';', 
                StringSplitOptions.RemoveEmptyEntries
            );

            var maskedParts = parts.Select(p =>
            {
                var kv = p.Split('=', 2);
                if (kv.Length != 2)
                {
                    return p;
                }

                var key = kv[0].Trim();
                var val = kv[1].Trim();

                if (key.Contains("Password", StringComparison.OrdinalIgnoreCase) ||
                    key.Contains("Secret", StringComparison.OrdinalIgnoreCase) ||
                    key.Contains("Token", StringComparison.OrdinalIgnoreCase))
                {
                    return $"{key}=********";
                }

                return p;
            });

            return string.Join("; ", maskedParts);
        }
    }
}
