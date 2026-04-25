using dvmig.Core.DataPreservation;
using dvmig.Core.Metadata;
using dvmig.Core.Provisioning;
using dvmig.Core.Seeding;
using dvmig.Core.Settings;
using dvmig.Core.Synchronization;
using dvmig.Providers;
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
        private static ISettingsService? _settingsService;
        private static ILogger? _logger;

        /// <summary>
        /// Main application loop for the CLI. Handles connection, entity 
        /// selection, and migration execution.
        /// </summary>
        /// <param name="args">Command line arguments.</param>
        static async Task Main(string[] args)
        {
            _logger = new Serilog.LoggerConfiguration()
                .WriteTo.File("dvmig-cli.log")
                .CreateLogger();

            _settingsService = new SettingsService();

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
                var choice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("What would you like to do?")
                        .PageSize(10)
                        .AddChoices(new[] {
                            "Migrate Data",
                            "Seed Test Data",
                            "Clean Target Environment",
                            "Exit"
                        }));

                switch (choice)
                {
                    case "Migrate Data":
                        await HandleMigrationAsync();
                        break;
                    case "Seed Test Data":
                        await HandleSeedingAsync();
                        break;
                    case "Clean Target Environment":
                        await HandleCleanupAsync();
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

            int count = AnsiConsole.Ask<int>("How many records to generate per entity (Account/Contact)?", 100);

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
            var provider = await ConnectAsync("Target Environment to Clean");
            if (provider == null) return;

            if (!AnsiConsole.Confirm("[red]Are you sure you want to remove all dvmig components (schema and plugins) from this environment?[/]", false))
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
                .StartAsync("Cleaning environment...", async ctx =>
                {
                    var progress = new Progress<string>(msg =>
                    {
                        AnsiConsole.MarkupLine($"[grey][[{DateTime.Now:HH:mm:ss}]][/] {msg}");
                    });

                    await _setupService.CleanEnvironmentAsync(provider, progress);
                });

            AnsiConsole.MarkupLine("[bold green]Cleanup Finished![/]");
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

                var query = new Microsoft.Xrm.Sdk.Query.QueryExpression(
                    logicalName
                )
                {
                    ColumnSet = new Microsoft.Xrm.Sdk.Query.ColumnSet(true)
                };

                var sourceRecords = await _source!.RetrieveMultipleAsync(
                    query,
                    default
                );

                if (sourceRecords.Entities.Count == 0)
                {
                    AnsiConsole.MarkupLine(
                        $"[grey]No records found for {logicalName}.[/]"
                    );

                    continue;
                }

                await AnsiConsole.Progress()
                    .StartAsync(async ctx =>
                    {
                        var task = ctx.AddTask(
                            $"Syncing {logicalName}",
                            true,
                            sourceRecords.Entities.Count
                        );

                        var recordProgress = new Progress<bool>(success =>
                        {
                            task.Increment(1);
                        });

                        await _engine!.SyncAsync(
                            sourceRecords.Entities,
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
