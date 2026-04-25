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
            _logger = LoggerInitializer.Initialize("dvmig.Cli");

            _settingsService = new SettingsService();
            _stateTracker = new LocalFileStateTracker();

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
                    "Seed Test Data",
                    "Uninstall dvmig Components from Target"
                };

                if (enableSourceCleanup)
                {
                    choices.Add("Wipe ALL Accounts, Contacts, and Activities from Source (DANGEROUS)");
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
                    case "Seed Test Data":
                        await HandleSeedingAsync();
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
                    AnsiConsole.MarkupLine("[grey]Press any key to return to menu...[/]");
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
            if (_source == null) return;

            // 2. Target Connection
            _target = await ConnectAsync("Target");
            if (_target == null) return;

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

        private static async Task HandleSeedingAsync()
        {
            var provider = await ConnectAsync("Source Environment to Seed");
            if (provider == null) return;

            int count = AnsiConsole.Ask<int>("How many [bold blue]Accounts[/] would you like to generate?", 100);

            _seeder ??= new TestDataSeeder(_logger!);

            await AnsiConsole.Status()
                .StartAsync("Seeding data...", async ctx =>
                {
                    var progress = new Progress<string>(msg =>
                    {
                        AnsiConsole.MarkupLine($"[grey][[{DateTime.Now:HH:mm:ss}]][/] {msg}");
                    });

                    await _seeder.SeedTestDataAsync(provider, count, progress);
                });

            AnsiConsole.MarkupLine("[bold green]Seeding Finished![/]");
        }

        private static async Task HandleCleanupAsync()
        {
            var provider = await ConnectAsync("Target Environment to Uninstall from");
            if (provider == null) return;

            if (!AnsiConsole.Confirm("[red]Are you sure you want to remove all dvmig system components (schema and plugins) from this environment?[/]", false))
            {
                return;
            }

            _setupService ??= new SetupService(
                new EnvironmentValidator(),
                new SchemaManager(_logger!),
                new PluginDeployer(_logger!),
                _logger!
            );

            await AnsiConsole.Status()
                .StartAsync("Uninstalling components...", async ctx =>
                {
                    var progress = new Progress<string>(msg =>
                    {
                        AnsiConsole.MarkupLine($"[grey][[{DateTime.Now:HH:mm:ss}]][/] {msg}");
                    });

                    await _setupService.CleanEnvironmentAsync(provider, progress);
                });

            AnsiConsole.MarkupLine("[bold green]Uninstallation Finished![/]");
        }

        private static async Task HandleSourceCleanupAsync()
        {
            var provider = await ConnectAsync("Source Environment to Wipe");
            if (provider == null) return;

            AnsiConsole.MarkupLine("[bold red]CRITICAL WARNING:[/] This operation will delete [bold]EVERY SINGLE[/] Account, Contact, Task, Phone Call, and Email record from the selected environment.");
            AnsiConsole.MarkupLine("[red]This is NOT restricted to test data. Real data will be destroyed.[/]");
            AnsiConsole.MarkupLine("[red]This action is permanent and irreversible.[/]");

            var confirmation = AnsiConsole.Ask<string>("Type [bold red]WIPE ALL DATA[/] to confirm:");
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
                        AnsiConsole.MarkupLine($"[grey][[{DateTime.Now:HH:mm:ss}]][/] {msg}");
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
            string? storedConn = label.Contains("Source", StringComparison.OrdinalIgnoreCase) 
                ? settings.SourceConnectionString 
                : settings.TargetConnectionString;

            string connStr;
            if (!string.IsNullOrEmpty(storedConn))
            {
                // Mask sensitive info for preview (passwords/secrets)
                var preview = MaskConnectionString(storedConn);
                
                var useStored = AnsiConsole.Confirm(
                    $"Use [green]stored[/] {label} connection string?\n[grey]({preview})[/]", 
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
                if (AnsiConsole.Confirm($"Save this {label} connection string for future use?", true))
                {
                    settings.RememberConnections = true;
                    if (label.Contains("Source", StringComparison.OrdinalIgnoreCase))
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
            return await AnsiConsole.Status()
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

                        var entities = response.EntityMetadata
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
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine(
                            $"[red]×[/] Failed to fetch metadata: {ex.Message}"
                        );

                        return null;
                    }
                });
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

                // Check for existing state and prompt to resume
                await _stateTracker!.InitializeAsync(
                    _source!.ConnectionString,
                    _target!.ConnectionString,
                    logicalName
                );

                if (_stateTracker.StateExists())
                {
                    var synced = await _stateTracker.GetSyncedIdsAsync();
                    if (synced.Count > 0)
                    {
                        if (!AnsiConsole.Confirm(
                            $"Previous migration state found for {logicalName} " +
                            $"({synced.Count} records already synced). Resume?", 
                            true))
                        {
                            await _stateTracker.ClearStateAsync();
                        }
                    }
                }

                var query = new Microsoft.Xrm.Sdk.Query.QueryExpression(
                    logicalName
                )
                {
                    ColumnSet = new Microsoft.Xrm.Sdk.Query.ColumnSet(true),
                    PageInfo = new Microsoft.Xrm.Sdk.Query.PagingInfo
                    {
                        Count = 1000,
                        PageNumber = 1
                    }
                };

                var allEntities = new List<Entity>();
                var alreadySyncedIds = new HashSet<Guid>();

                await AnsiConsole.Status()
                    .StartAsync($"Fetching {logicalName} records...", async ctx =>
                    {
                        while (true)
                        {
                            var response = await _source!.RetrieveMultipleAsync(
                                query,
                                default
                            );
                            
                            allEntities.AddRange(response.Entities);

                            if (!response.MoreRecords)
                            {
                                break;
                            }

                            query.PageInfo.PageNumber++;
                            query.PageInfo.PagingCookie = response.PagingCookie;
                            
                            ctx.Status(
                                $"Fetching {logicalName} records " +
                                $"(Page {query.PageInfo.PageNumber}, {allEntities.Count} so far)..."
                            );
                        }

                        alreadySyncedIds = await _stateTracker.GetSyncedIdsAsync();
                    });

                if (allEntities.Count == 0)
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
                        int current = alreadySyncedIds.Count;
                        int total = allEntities.Count;

                        var task = ctx.AddTask(
                            $"Syncing {logicalName} ({current}/{total})",
                            true,
                            total
                        );

                        task.Value = current;

                        var recordProgress = new Progress<bool>(success =>
                        {
                            current++;
                            task.Value = current;
                            task.Description = $"Syncing {logicalName} ({current}/{total})";
                        });

                        await _engine!.SyncAsync(
                            allEntities,
                            new SyncOptions { StripMissingDependencies = true },
                            null,
                            recordProgress,
                            default
                        );
                    });
            }
        }

        private static string MaskConnectionString(string connectionString)
        {
            var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries);
            var maskedParts = parts.Select(p =>
            {
                var kv = p.Split('=', 2);
                if (kv.Length != 2) return p;

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
