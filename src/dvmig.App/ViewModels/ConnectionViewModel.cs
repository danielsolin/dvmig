using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using dvmig.App.Services;
using dvmig.Core;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace dvmig.App.ViewModels
{
    public partial class ConnectionViewModel : ViewModelBase
    {
        private readonly INavigationService _navigationService;
        private readonly IMigrationService _migrationService;
        private readonly ISettingsService _settingsService;
        private readonly ISetupService _setupService;

        [ObservableProperty]
        private string _sourceConnectionString = string.Empty;

        [ObservableProperty]
        private string _targetConnectionString = string.Empty;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ProceedToSelectionCommand))]
        private bool _isSourceConnected;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ProceedToSelectionCommand))]
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

        [ObservableProperty]
        private bool _autoConnect;

        private bool _isSettingsLoading;

        partial void OnRememberConnectionsChanged(bool value) => SaveCurrentSettings();
        partial void OnAutoConnectChanged(bool value) => SaveCurrentSettings();

        private void SaveCurrentSettings()
        {
            if (_isSettingsLoading)
            {
                return;
            }

            _settingsService.SaveSettings(
                new UserSettings
                {
                    RememberConnections = RememberConnections,
                    AutoConnect = AutoConnect,
                    SourceConnectionString = SourceConnectionString,
                    TargetConnectionString = TargetConnectionString
                }
            );
        }

        [ObservableProperty]
        private bool _isEnvironmentReady = true;

        [ObservableProperty]
        private bool _isInitializing;

        [ObservableProperty]
        private string _initializationStatus = string.Empty;

        private CancellationTokenSource? _sourceCts;
        private CancellationTokenSource? _targetCts;

        public ConnectionViewModel(
            INavigationService navigationService,
            IMigrationService migrationService,
            ISettingsService settingsService,
            ISetupService setupService
        )
        {
            _navigationService = navigationService;
            _migrationService = migrationService;
            _settingsService = settingsService;
            _setupService = setupService;

            LoadSavedSettings();
        }

        private void LoadSavedSettings()
        {
            _isSettingsLoading = true;

            try
            {
                var settings = _settingsService.LoadSettings();
                RememberConnections = settings.RememberConnections;
                AutoConnect = settings.AutoConnect;

                if (RememberConnections)
                {
                    SourceConnectionString = settings.SourceConnectionString;
                    TargetConnectionString = settings.TargetConnectionString;

                    if (_migrationService.SourceProvider != null)
                    {
                        IsSourceConnected = true;
                        SourceStatus = "Connected";
                    }

                    if (_migrationService.TargetProvider != null)
                    {
                        IsTargetConnected = true;
                        TargetStatus = "Connected";
                    }

                    if (AutoConnect)
                    {
                        if (_migrationService.SourceProvider == null &&
                            !string.IsNullOrEmpty(SourceConnectionString))
                        {
                            _ = TestSourceConnectionAsync();
                        }

                        if (_migrationService.TargetProvider == null &&
                            !string.IsNullOrEmpty(TargetConnectionString))
                        {
                            _ = TestTargetConnectionAsync();
                        }
                    }
                }
            }
            finally
            {
                _isSettingsLoading = false;
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
                bool isLegacy =
                    SourceConnectionString.Contains("AuthType=AD") ||
                    SourceConnectionString.Contains("AuthType=IFD");

                var result = await _migrationService.ConnectSourceAsync(
                    SourceConnectionString,
                    isLegacy,
                    _sourceCts.Token
                );

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
        private void DisconnectSource()
        {
            _migrationService.DisconnectSource();
            IsSourceConnected = false;
            SourceStatus = "Not Connected";
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
                    TargetConnectionString,
                    false,
                    _targetCts.Token
                );

                IsTargetConnected = result;
                TargetStatus = result ? "Connected" : "Failed";

                if (result)
                {
                    IsEnvironmentReady = await _setupService.IsEnvironmentReadyAsync(
                        _migrationService.TargetProvider!,
                        _targetCts.Token
                    );
                }
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
        private void DisconnectTarget()
        {
            _migrationService.DisconnectTarget();
            IsTargetConnected = false;
            TargetStatus = "Not Connected";
        }

        [RelayCommand]
        private async Task InitializeEnvironmentAsync()
        {
            if (_migrationService.TargetProvider == null)
            {
                return;
            }

            IsInitializing = true;
            InitializationStatus = "Initializing...";

            var progress = new Progress<string>(msg =>
            {
                InitializationStatus = msg;
            });

            try
            {
                await _setupService.CreateSchemaAsync(
                    _migrationService.TargetProvider,
                    progress
                );

                var assemblyPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "..",
                    "..",
                    "..",
                    "..",
                    "dvmig.Plugins",
                    "bin",
                    "Debug",
                    "netstandard2.0",
                    "dvmig.Plugins.dll"
                );

                await _setupService.DeployPluginAsync(
                    _migrationService.TargetProvider,
                    assemblyPath,
                    progress
                );

                IsEnvironmentReady = true;
                InitializationStatus = "Environment initialized successfully.";
                MessageBox.Show(
                    "Environment initialized successfully.",
                    "Success",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
            catch (Exception ex)
            {
                InitializationStatus = $"Initialization failed: {ex.Message}";
                MessageBox.Show(
                    $"Initialization failed: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
            finally
            {
                IsInitializing = false;
            }
        }

        [RelayCommand(CanExecute = nameof(CanProceed))]
        private void ProceedToSelection()
        {
            SaveCurrentSettings();
            _navigationService.NavigateTo<EntitySelectionViewModel>();
        }


        private bool CanProceed() => IsSourceConnected && IsTargetConnected;
    }
}
