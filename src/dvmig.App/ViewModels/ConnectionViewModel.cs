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
        private readonly ISettingsService _settingsService;

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

        [ObservableProperty]
        private bool _rememberConnections;

        public ConnectionViewModel(
            INavigationService navigationService,
            IMigrationService migrationService,
            ISettingsService settingsService)
        {
            _navigationService = navigationService;
            _migrationService = migrationService;
            _settingsService = settingsService;

            LoadSavedSettings();
        }

        private void LoadSavedSettings()
        {
            var settings = _settingsService.LoadSettings();
            RememberConnections = settings.RememberConnections;
            
            if (RememberConnections)
            {
                SourceConnectionString = settings.SourceConnectionString;
                TargetConnectionString = settings.TargetConnectionString;
            }
        }

        [RelayCommand]
        private async Task TestSourceConnectionAsync()
        {
            SourceStatus = "Connecting...";
            bool isLegacy = SourceConnectionString.Contains("AuthType=AD") || 
                           SourceConnectionString.Contains("AuthType=IFD");
            
            var result = await _migrationService.ConnectSourceAsync(
                SourceConnectionString, isLegacy);
            
            IsSourceConnected = result;
            SourceStatus = result ? "Connected" : "Failed";
        }

        [RelayCommand]
        private async Task TestTargetConnectionAsync()
        {
            TargetStatus = "Connecting...";
            var result = await _migrationService.ConnectTargetAsync(
                TargetConnectionString, false);
            
            IsTargetConnected = result;
            TargetStatus = result ? "Connected" : "Failed";
        }

        [RelayCommand]
        private void ProceedToSelection()
        {
            if (RememberConnections)
            {
                _settingsService.SaveSettings(new UserSettings
                {
                    SourceConnectionString = SourceConnectionString,
                    TargetConnectionString = TargetConnectionString,
                    RememberConnections = true
                });
            }
            else
            {
                _settingsService.SaveSettings(new UserSettings
                {
                    RememberConnections = false
                });
            }

            _navigationService.NavigateTo<EntitySelectionViewModel>();
        }
    }
}
