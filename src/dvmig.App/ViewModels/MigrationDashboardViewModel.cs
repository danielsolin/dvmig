using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using dvmig.App.Models;
using dvmig.App.Services;
using dvmig.Core;
using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace dvmig.App.ViewModels
{
    public partial class MigrationDashboardViewModel : ViewModelBase
    {
        private readonly INavigationService _navigationService;
        private readonly IMigrationService _migrationService;
        // private readonly ISyncEngine _syncEngine; // We'll inject this

        [ObservableProperty]
        private SyncProgressInfo _progress = new SyncProgressInfo();

        public ObservableCollection<string> Logs { get; } = 
            new ObservableCollection<string>();

        public MigrationDashboardViewModel(
            INavigationService navigationService,
            IMigrationService migrationService)
        {
            _navigationService = navigationService;
            _migrationService = migrationService;
        }

        [RelayCommand]
        private async Task StartMigrationAsync()
        {
            Logs.Add("Starting migration...");
            Progress.TotalRecords = 100; // Simulation
            
            for (int i = 1; i <= 100; i++)
            {
                await Task.Delay(50); // Simulate work
                Progress.Update(i, i, 0);
                if (i % 10 == 0)
                {
                    Logs.Insert(0, $"[{DateTime.Now:HH:mm:ss}] Processed {i} records...");
                }
            }
            Logs.Insert(0, "Migration completed successfully!");
        }
    }
}
