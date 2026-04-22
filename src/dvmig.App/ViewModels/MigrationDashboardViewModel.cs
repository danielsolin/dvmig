using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using dvmig.App.Models;
using dvmig.App.Services;
using dvmig.Core;
using Microsoft.Xrm.Sdk.Query;
using System.Collections.ObjectModel;

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
                Logs.Insert(0,
                    "Error: Source or Target provider not connected.");

                return;
            }

            IsMigrationRunning = true;
            _cts = new CancellationTokenSource();

            IProgress<string> progressReporter = new Progress<string>(msg =>
            {
                Logs.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {msg}");
            });

            try
            {
                var entitiesToMigrate = _migrationService.SelectedEntities;

                foreach (var logicalName in entitiesToMigrate)
                {
                    _cts.Token.ThrowIfCancellationRequested();

                    progressReporter.Report(
                        $"Fetching {logicalName} records...");

                    var query = new QueryExpression(logicalName)
                    {
                        ColumnSet = new ColumnSet(true)
                    };

                    var sourceRecords = await _migrationService.SourceProvider
                        .RetrieveMultipleAsync(query, _cts.Token);

                    if (sourceRecords.Entities.Count == 0)
                    {
                        progressReporter.Report(
                            $"No records for {logicalName}.");

                        continue;
                    }

                    Progress.CurrentEntity = logicalName;
                    Progress.TotalRecords = sourceRecords.Entities.Count;
                    Progress.Update(0, 0, 0);

                    await _syncEngine.SyncAsync(
                        sourceRecords.Entities,
                        new SyncOptions
                        {
                            StripMissingDependencies = true
                        },
                        progressReporter,
                        _cts.Token);
                }

                progressReporter.Report("Migration finished.");
            }
            catch (OperationCanceledException)
            {
                progressReporter.Report("MIGRATION CANCELLED BY USER.");
            }
            catch (Exception ex)
            {
                progressReporter.Report($"CRITICAL ERROR: {ex.Message}");
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
            Logs.Insert(0, $"[{now}] Cancellation requested...");
        }

        private bool CanStartMigration() => !IsMigrationRunning;
    }
}
