using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using dvmig.App.Services;

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
        private bool _isSourceConnecting;

        [ObservableProperty]
        private bool _isTargetConnecting;

        [ObservableProperty]
        private string _sourceStatus = "Not Connected";

        [ObservableProperty]
        private string _targetStatus = "Not Connected";

        [ObservableProperty]
        private bool _rememberConnections;

        private CancellationTokenSource? _sourceCts;
        private CancellationTokenSource? _targetCts;

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
            _sourceCts?.Cancel();
            _sourceCts = new CancellationTokenSource();
            
            IsSourceConnecting = true;
            SourceStatus = "Connecting...";
            
            try
            {
                bool isLegacy = SourceConnectionString.Contains("AuthType=AD") ||
                               SourceConnectionString.Contains("AuthType=IFD");

                var result = await _migrationService.ConnectSourceAsync(
                    SourceConnectionString, isLegacy, _sourceCts.Token);

                IsSourceConnected = result;
                SourceStatus = result ? "Connected" : "Failed";
            }
            finally
            {
                IsSourceConnecting = false;
            }
        }

        [RelayCommand]
        private void CancelSourceConnection()
        {
            _sourceCts?.Cancel();
            SourceStatus = "Cancelled";
        }

        [RelayCommand]
        private async Task TestTargetConnectionAsync()
        {
            _targetCts?.Cancel();
            _targetCts = new CancellationTokenSource();
            
            IsTargetConnecting = true;
            TargetStatus = "Connecting...";
            
            try
            {
                var result = await _migrationService.ConnectTargetAsync(
                    TargetConnectionString, false, _targetCts.Token);

                IsTargetConnected = result;
                TargetStatus = result ? "Connected" : "Failed";
            }
            finally
            {
                IsTargetConnecting = false;
            }
        }

        [RelayCommand]
        private void CancelTargetConnection()
        {
            _targetCts?.Cancel();
            TargetStatus = "Cancelled";
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
