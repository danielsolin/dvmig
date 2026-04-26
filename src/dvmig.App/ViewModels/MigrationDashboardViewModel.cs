using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using dvmig.App.Models;
using dvmig.App.Services;
using dvmig.Core.Synchronization;
using Microsoft.Xrm.Sdk.Query;

namespace dvmig.App.ViewModels
{
    /// <summary>
    /// View model for the migration dashboard, providing real-time progress
    /// tracking and logging for the migration process.
    /// </summary>
    public partial class MigrationDashboardViewModel : ViewModelBase
    {
        private readonly INavigationService _navigationService;
        private readonly IMigrationService _migrationService;
        private readonly ISyncEngine _syncEngine;
        private readonly ISyncStateTracker _stateTracker;
        private CancellationTokenSource? _cts;

        /// <summary>
        /// Gets or sets the progress information for the current migration.
        /// </summary>
        [ObservableProperty]
        private SyncProgressInfo _progress = new SyncProgressInfo();

        /// <summary>
        /// Gets or sets a value indicating whether a migration process is 
        /// currently running.
        /// </summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StartMigrationCommand))]
        [NotifyCanExecuteChangedFor(nameof(CancelMigrationCommand))]
        [NotifyCanExecuteChangedFor(nameof(GoBackCommand))]
        private bool _isMigrationRunning;

        /// <summary>
        /// Gets the collection of log messages generated during the 
        /// migration process.
        /// </summary>
        public ObservableCollection<string> Logs { get; } =
            new ObservableCollection<string>();

        /// <param name="navigationService">The navigation service.</param>
        /// <param name="migrationService">The migration service.</param>
        /// <param name="syncEngine">The synchronization engine.</param>
        /// <param name="stateTracker">The sync state tracking service.</param>
        public MigrationDashboardViewModel(
            INavigationService navigationService,
            IMigrationService migrationService,
            ISyncEngine syncEngine,
            ISyncStateTracker stateTracker)
        {
            _navigationService = navigationService;
            _migrationService = migrationService;
            _syncEngine = syncEngine;
            _stateTracker = stateTracker;
        }

        /// <summary>
        /// Initiates the migration process asynchronously.
        /// </summary>
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
                    progressReporter.Report($"Processing {logicalName}...");

                    // 1. Initialize Engine for this entity
                    await _syncEngine.InitializeEntitySyncAsync(logicalName);

                    if (_stateTracker.StateExists())
                    {
                        var syncedIds = await _stateTracker.GetSyncedIdsAsync();
                        if (syncedIds.Count > 0)
                        {
                            var result = MessageBox.Show(
                                $"Previous migration state found for {logicalName} " +
                                $"({syncedIds.Count} records already synced). \n\n" +
                                "Do you want to resume from the checkpoint?",
                                "Resume Migration",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Question
                            );

                            if (result == MessageBoxResult.No)
                            {
                                await _stateTracker.ClearStateAsync();
                                await _syncEngine.InitializeEntitySyncAsync(logicalName);
                            }
                            else
                            {
                                // Account for already synced records in overall progress
                                cumulativeProcessed += syncedIds.Count;
                                cumulativeSuccess += syncedIds.Count;

                                Progress.Update(
                                    cumulativeProcessed,
                                    cumulativeSuccess,
                                    cumulativeFailure
                                );
                            }
                        }
                    }

                    QueryExpression? query = null;
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

                        query = new QueryExpression(logicalName);
                        query.Criteria.AddCondition(
                            primaryId,
                            ConditionOperator.In,
                            config.SelectedRecordIds
                                .Select(id => (object)id)
                                .ToArray()
                        );
                    }

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

                    // 2. Centralized Paginated Sync
                    await _syncEngine.SyncEntityAsync(
                        logicalName,
                        new SyncOptions { StripMissingDependencies = true },
                        query,
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
