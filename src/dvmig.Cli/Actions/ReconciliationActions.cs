using dvmig.Cli.Infrastructure;
using dvmig.Core.Interfaces;
using dvmig.Core.Synchronization;
using Serilog;
using Spectre.Console;

namespace dvmig.Cli.Actions
{
    public class ReconciliationActions
    {
        private readonly ConnectionManager _connectionManager;
        private readonly IReconciliationService _reconciliationService;
        private readonly ILogger _logger;

        public ReconciliationActions(
            ConnectionManager connectionManager,
            IReconciliationService reconciliationService,
            ILogger logger
        )
        {
            _connectionManager = connectionManager;
            _reconciliationService = reconciliationService;
            _logger = logger;
        }

        public async Task HandleReconcileAsync()
        {
            var target = await _connectionManager.ConnectAsync(
                ConnectionDirection.Target
            );

            if (target == null)
                return;

            bool isInitialized =
                await _reconciliationService.IsInitializedAsync(
                    target,
                    default
                );

            if (!isInitialized)
            {
                CliUI.WriteWarning(
                    "Migration failure logging is not initialized on " +
                    "this target."
                );
                AnsiConsole.MarkupLine(
                    "[grey]Please use 'Install/Update dvmig Components' " +
                    "to enable this feature.[/]"
                );

                return;
            }

            var failures = await CliUI.RunStatusAsync(
                "Fetching recorded migration failures...",
                async () =>
                    await _reconciliationService.GetFailuresAsync(
                        target,
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
                table.AddRow(
                    failure.EntityLogicalName,
                    failure.SourceId,
                    failure.TimestampUtc.ToString("yyyy-MM-dd HH:mm:ss"),
                    failure.ErrorMessage
                );

            AnsiConsole.Write(table);

            var clearLog = "Would you like to clear the failure log on " +
                           "the target?";

            if (AnsiConsole.Confirm(clearLog, false))
            {
                await CliUI.RunStatusAsync(
                    "Clearing failure log...",
                    async _ =>
                        await _reconciliationService.ClearFailuresAsync(
                            target,
                            default
                        )
                );

                CliUI.WriteSuccess("Failure log cleared.");
            }
        }
    }
}
