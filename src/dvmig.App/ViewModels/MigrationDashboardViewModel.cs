using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using dvmig.App.Models;
using dvmig.App.Services;
using dvmig.Core;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace dvmig.App.ViewModels
{
    public partial class MigrationDashboardViewModel : ViewModelBase
    {
        private readonly INavigationService _navigationService;
        private readonly IMigrationService _migrationService;
        private readonly ISyncEngine _syncEngine;

        [ObservableProperty]
        private SyncProgressInfo _progress = new SyncProgressInfo();

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

        [RelayCommand]
        private async Task StartMigrationAsync()
        {
            if (_migrationService.SourceProvider == null || 
                _migrationService.TargetProvider == null)
            {
                Logs.Add("Error: Source or Target provider not connected.");
                
                return;
            }

            var progressReporter = new Progress<string>(msg => 
            {
                Logs.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {msg}");
            });

            var entitiesToMigrate = _migrationService.SelectedEntities;
            
            foreach (var logicalName in entitiesToMigrate)
            {
                progressReporter.Report($"Fetching {logicalName} records from source...");
                
                var query = new QueryExpression(logicalName) { ColumnSet = new ColumnSet(true) };
                var sourceRecords = await _migrationService.SourceProvider.RetrieveMultipleAsync(query);
                
                if (sourceRecords.Entities.Count == 0)
                {
                    progressReporter.Report($"No records found for {logicalName}. Skipping.");
                    
                    continue;
                }

                Progress.CurrentEntity = logicalName;
                Progress.TotalRecords = sourceRecords.Entities.Count;
                Progress.Update(0, 0, 0);

                await _syncEngine.SyncAsync(
                    sourceRecords.Entities, 
                    new SyncOptions { UseBulk = true }, 
                    progressReporter);
            }

            progressReporter.Report("All selected entities processed.");
        }
    }
}
