using dvmig.Cli.Infrastructure;
using dvmig.Core.Interfaces;
using dvmig.Core.Provisioning;
using dvmig.Core.Seeding;
using Serilog;
using Spectre.Console;

namespace dvmig.Cli.Actions
{
    public class MaintenanceActions
    {
        private readonly ConnectionManager _connectionManager;
        private readonly ITestDataSeeder _seeder;
        private readonly ISetupService _setupService;
        private readonly ILogger _logger;

        public MaintenanceActions(
            ConnectionManager connectionManager,
            ITestDataSeeder seeder,
            ISetupService setupService,
            ILogger logger)
        {
            _connectionManager = connectionManager;
            _seeder = seeder;
            _setupService = setupService;
            _logger = logger;
        }

        public async Task HandleSeedingAsync()
        {
            var provider = await _connectionManager.ConnectAsync(
                ConnectionDirection.Source
            );

            if (provider == null)
            {
                return;
            }

            var prompt = "How many [bold blue]Accounts[/] would you like " +
                         "to generate?";

            int count = AnsiConsole.Ask<int>(prompt, 100);

            await CliUI.RunStatusAsync(
                "Seeding data...",
                async progress => await _seeder.SeedTestDataAsync(
                    provider,
                    count,
                    progress
                )
            );

            CliUI.WriteSuccess("Seeding Finished!");
        }

        public async Task HandleInstallAsync()
        {
            var provider = await _connectionManager.ConnectAsync(
                ConnectionDirection.Target
            );

            if (provider == null)
            {
                return;
            }

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

        public async Task HandleTargetComponentsCleanupAsync()
        {
            var provider = await _connectionManager.ConnectAsync(
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

            await CliUI.RunStatusAsync(
                "Uninstalling components...",
                async progress => await _setupService.CleanEnvironmentAsync(
                    provider,
                    progress
                )
            );

            CliUI.WriteSuccess("Uninstallation Finished!");
        }

        public async Task HandleSourceTestDataCleanupAsync()
        {
            var provider = await _connectionManager.ConnectAsync(
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
                async progress => await _seeder.CleanTestDataAsync(
                    provider,
                    progress
                )
            );

            CliUI.WriteSuccess("Data Wipe Finished!");
        }
    }
}
