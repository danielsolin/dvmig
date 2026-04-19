using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using dvmig.App.Services;
using System;
using System.Threading.Tasks;

namespace dvmig.App.ViewModels
{
    public partial class ConnectionViewModel : ViewModelBase
    {
        private readonly INavigationService _navigationService;
        private readonly IMigrationService _migrationService;

        [ObservableProperty]
        private string _sourceConnectionString = string.Empty;

        [ObservableProperty]
        private string _targetConnectionString = string.Empty;

        [ObservableProperty]
        private bool _isSourceConnected;

        [ObservableProperty]
        private bool _isTargetConnected;

        [ObservableProperty]
        private string _sourceStatus = "Not Connected";

        [ObservableProperty]
        private string _targetStatus = "Not Connected";

        public ConnectionViewModel(
            INavigationService navigationService,
            IMigrationService migrationService)
        {
            _navigationService = navigationService;
            _migrationService = migrationService;
        }

        [RelayCommand]
        private async Task TestSourceConnectionAsync()
        {
            SourceStatus = "Connecting...";
            // Heuristic to detect legacy (simple check for now)
            bool isLegacy = SourceConnectionString.Contains("AuthType=AD") || 
                           SourceConnectionString.Contains("AuthType=IFD");
            
            var result = await _migrationService.TestConnectionAsync(
                SourceConnectionString, isLegacy);
            
            IsSourceConnected = result;
            SourceStatus = result ? "Connected" : "Failed";
        }

        [RelayCommand]
        private async Task TestTargetConnectionAsync()
        {
            TargetStatus = "Connecting...";
            var result = await _migrationService.TestConnectionAsync(
                TargetConnectionString, false);
            
            IsTargetConnected = result;
            TargetStatus = result ? "Connected" : "Failed";
        }

        [RelayCommand]
        private void ProceedToSelection()
        {
            _navigationService.NavigateTo<EntitySelectionViewModel>();
        }
    }
}
