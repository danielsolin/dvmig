using dvmig.Core;
using dvmig.Providers;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Messages;
using Serilog;
using Spectre.Console;

namespace dvmig.Cli
{
    class Program
    {
        private static IDataverseProvider? _source;
        private static IDataverseProvider? _target;
        private static ISyncEngine? _engine;

        static async Task Main(string[] args)
        {
            AnsiConsole.Write(
                new FigletText("DVMIG")
                    .Color(Color.Blue)
            );

            AnsiConsole.MarkupLine("[bold]Dataverse Migrator - Technical UI[/]");
            AnsiConsole.WriteLine();

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

            // 3. Initialize Engine
            var logger = Serilog.Log.Logger = new Serilog.LoggerConfiguration()
                .WriteTo.File("dvmig-cli.log")
                .CreateLogger();

            // Minimal UserMapper for now (no mapping)
            var userMapper = new UserMapper(_source, _target, logger);
            var dataPreservation = new DataPreservationManager(
                _target,
                logger
            );

            _engine = new SyncEngine(
                _source,
                _target,
                userMapper,
                dataPreservation,
                logger
            );

            // 4. Entity Selection
            var selectedEntities = await SelectEntitiesAsync();
            if (selectedEntities == null || selectedEntities.Count == 0)
            {
                AnsiConsole.MarkupLine(
                    "[yellow]No entities selected. Exiting.[/]"
                );

                return;
            }

            // 5. Run Migration
            await RunMigrationAsync(selectedEntities);

            AnsiConsole.MarkupLine("[bold green]Migration Finished![/]");
            AnsiConsole.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }

        private static async Task<IDataverseProvider?> ConnectAsync(
            string label)
        {
            var connStr = AnsiConsole.Ask<string>(
                $"Enter [bold blue]{label}[/] Connection String:"
            );

            var isLegacy = AnsiConsole.Confirm(
                $"Is [bold blue]{label}[/] Legacy CRM (OnPrem)?",
                false
            );

            return await AnsiConsole.Status()
                .StartAsync($"Connecting to {label}...", async ctx =>
                {
                    try
                    {
                        IDataverseProvider provider = isLegacy
                            ? new LegacyCrmProvider(connStr)
                            : new DataverseProvider(connStr);

                        // Test connection
                        await provider.ExecuteAsync(
                            new WhoAmIRequest(),
                            default
                        );

                        AnsiConsole.MarkupLine(
                            $"[green]✓[/] Connected to {label}"
                        );

                        return provider;
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
        }

        private static async Task<List<string>?> SelectEntitiesAsync()
        {
            return await AnsiConsole.Status()
                .StartAsync("Fetching entity metadata...", async ctx =>
                {
                    try
                    {
                        var request = new RetrieveAllEntitiesRequest
                        {
                            EntityFilters = Microsoft.Xrm.Sdk.Metadata.EntityFilters.Entity,
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

        private static bool IsStandardEntity(string logicalName)
        {
            var standard = new[]
            {
                "account",
                "contact",
                "lead",
                "opportunity",
                "task",
                "phonecall",
                "email",
                "appointment",
                "incident",
                "product",
                "pricelevel",
                "quote",
                "salesorder",
                "invoice",
                "competitor",
                "equipment",
                "businessunit",
                "team"
            };

            return standard.Contains(logicalName.ToLower());
        }

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
    }
}
