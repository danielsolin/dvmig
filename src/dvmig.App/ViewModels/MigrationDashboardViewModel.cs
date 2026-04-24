using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using dvmig.App.Models;
using dvmig.App.Services;
using dvmig.Core.Synchronization;
using Microsoft.Xrm.Sdk.Query;

namespace dvmig.App.ViewModels
{
    public partial class MigrationDashboardViewModel : ViewModelBase
    {
        private readonly INavigationService _navigationService;
        private readonly IMigrationService _migrationService;
        private readonly ISyncEngine _syncEngine;
        private CancellationTokenSource? _cts;

        [ObservableProperty]
        private SyncProgressInfo _progress = new SyncProgressInfo();

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StartMigrationCommand))]
        [NotifyCanExecuteChangedFor(nameof(CancelMigrationCommand))]
        [NotifyCanExecuteChangedFor(nameof(GoBackCommand))]
        private bool _isMigrationRunning;

        public ObservableCollection<string> Logs { get; } =
            new ObservableCollection<string>();

        public MigrationDashboardViewModel(
            INavigationService navigationService,
            IMigrationService migrationService,
            ISyncEngine syncEngine)
        {
            _navigationService = navigationService;
            _migrationService = migrationService;
            _syncEngine = syncEngine;
        }

        [RelayCommand(CanExecute = nameof(CanStartMigration))]
        private async Task StartMigrationAsync()
        {
            if (_migrationService.SourceProvider == null ||
                _migrationService.TargetProvider == null)
            {
                Logs.Add("Error: Source or Target provider not connected.");

                return;
            }

            IsMigrationRunning = true;
            _cts = new CancellationTokenSource();

            IProgress<string> progressReporter = new Progress<string>(msg =>
            {
                Logs.Add($"[{DateTime.Now:HH:mm:ss}] {msg}");
            });

            try
            {
                var entitiesToMigrate = _migrationService.SelectedEntities;
                var sourceMetadata = await _migrationService
                    .GetSourceEntitiesAsync(_cts.Token);

                // Calculate total records for overall progress
                int overallTotal = 0;
                foreach (var config in entitiesToMigrate)
                {
                    if (config.SyncAllRecords)
                    {
                        overallTotal += (int)await _migrationService
                            .GetRecordCountAsync(config.LogicalName, _cts.Token);
                    }
                    else
                    {
                        overallTotal += config.SelectedRecordIds.Count;
                    }
                }

                Progress.TotalRecords = overallTotal;
                Progress.Update(0, 0, 0);
                StatusText = $"Migrating {overallTotal} records...";

                int cumulativeProcessed = 0;
                int cumulativeSuccess = 0;
                int cumulativeFailure = 0;

                foreach (var config in entitiesToMigrate)
                {
                    var logicalName = config.LogicalName;
                    _cts.Token.ThrowIfCancellationRequested();

                    Progress.CurrentEntity = logicalName;
                    progressReporter.Report(
                        $"Processing {logicalName}..."
                    );

                    var query = new QueryExpression(logicalName)
                    {
                        ColumnSet = new ColumnSet(true)
                    };

                    if (!config.SyncAllRecords)
                    {
                        if (!config.SelectedRecordIds.Any())
                        {
                            progressReporter.Report(
                                $"Skipping {logicalName}: No records selected."
                            );

                            continue;
                        }

                        var meta = sourceMetadata.FirstOrDefault(m =>
                            m.LogicalName == logicalName
                        );

                        var primaryId = meta?.PrimaryIdAttribute ??
                                        $"{logicalName}id";

                        query.Criteria.AddCondition(
                            primaryId,
                            ConditionOperator.In,
                            config.SelectedRecordIds
                                .Select(id => (object)id)
                                .ToArray()
                        );
                    }

                    var sourceRecords = await _migrationService.SourceProvider
                        .RetrieveMultipleAsync(query, _cts.Token);

                    if (sourceRecords.Entities.Count == 0)
                    {
                        progressReporter.Report(
                            $"No records found for {logicalName}."
                        );

                        continue;
                    }

                    progressReporter.Report(
                        $"Starting migration of {sourceRecords.Entities.Count} " +
                        $"{logicalName} records..."
                    );

                    var recordProgress = new Progress<bool>(success =>
                    {
                        cumulativeProcessed++;
                        if (success)
                        {
                            cumulativeSuccess++;
                        }
                        else
                        {
                            cumulativeFailure++;
                        }

                        Progress.Update(
                            cumulativeProcessed,
                            cumulativeSuccess,
                            cumulativeFailure
                        );
                    });

                    await _syncEngine.SyncAsync(
                        sourceRecords.Entities,
                        new SyncOptions
                        {
                            StripMissingDependencies = true
                        },
                        progressReporter,
                        recordProgress,
                        _cts.Token
                    );
                }

                progressReporter.Report("Migration finished.");
                StatusText = "Migration completed.";
            }
            catch (OperationCanceledException)
            {
                progressReporter.Report("MIGRATION CANCELLED BY USER.");
                StatusText = "Migration cancelled.";
            }
            catch (Exception ex)
            {
                progressReporter.Report($"CRITICAL ERROR: {ex.Message}");
                StatusText = "Migration failed.";
            }
            finally
            {
                IsMigrationRunning = false;
                _cts.Dispose();
                _cts = null;
            }
        }

        [RelayCommand(CanExecute = nameof(IsMigrationRunning))]
        private void CancelMigration()
        {
            _cts?.Cancel();

            var now = DateTime.Now.ToString("HH:mm:ss");
            Logs.Add($"[{now}] Cancellation requested...");
        }

        [RelayCommand(CanExecute = nameof(CanStartMigration))]
        private void GoBack()
        {
            _navigationService.NavigateTo<EntitySelectionViewModel>();
        }

        private bool CanStartMigration()
        {
            return !IsMigrationRunning;
        }
    }
}
