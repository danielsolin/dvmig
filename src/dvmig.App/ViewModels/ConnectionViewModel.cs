using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using dvmig.App.Services;
using dvmig.Core.Interfaces;
using dvmig.Core.Settings;

namespace dvmig.App.ViewModels
{
    /// <summary>
    /// View model for the connection screen, handling the configuration 
    /// and testing of source and target environment connections.
    /// </summary>
    public partial class ConnectionViewModel : ViewModelBase
    {
        private const string StatusNotConnected = "Not Connected";
        private const string StatusConnecting = "Connecting...";
        private const string StatusConnected = "Connected";
        private const string StatusFailed = "Failed";
        private const string StatusCancelled = "Cancelled";
        private const string StatusInitializing = "Initializing...";
        private const string StatusInitSuccess =
            "Environment initialized successfully.";
        private const string StatusInitFailedPrefix = "Initialization failed: ";

        private readonly INavigationService _navigationService;
        private readonly IMigrationService _migrationService;
        private readonly ISettingsService _settingsService;
        private readonly ISetupService _setupService;

        /// <summary>
        /// Gets or sets the connection string for the source environment.
        /// </summary>
        [ObservableProperty]
        private string _sourceConnectionString = string.Empty;

        /// <summary>
        /// Gets or sets the connection string for the target environment.
        /// </summary>
        [ObservableProperty]
        private string _targetConnectionString = string.Empty;

        /// <summary>
        /// Gets or sets a value indicating whether the source environment is 
        /// currently connected.
        /// </summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ProceedToSelectionCommand))]
        private bool _isSourceConnected;

        /// <summary>
        /// Gets or sets a value indicating whether the target environment is 
        /// currently connected.
        /// </summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ProceedToSelectionCommand))]
        private bool _isTargetConnected;

        /// <summary>
        /// Gets or sets a value indicating whether a connection attempt to 
        /// the source environment is in progress.
        /// </summary>
        [ObservableProperty]
        private bool _isSourceConnecting;

        /// <summary>
        /// Gets or sets a value indicating whether a connection attempt to 
        /// the target environment is in progress.
        /// </summary>
        [ObservableProperty]
        private bool _isTargetConnecting;

        /// <summary>
        /// Gets or sets the connection status message for the source 
        /// environment.
        /// </summary>
        [ObservableProperty]
        private string _sourceStatus = StatusNotConnected;

        /// <summary>
        /// Gets or sets the connection status message for the target
        /// environment.
        /// </summary>
        [ObservableProperty]
        private string _targetStatus = StatusNotConnected;

        /// <summary>
        /// Gets or sets a value indicating whether connection settings 
        /// should be persisted.
        /// </summary>
        [ObservableProperty]
        private bool _rememberConnections;

        /// <summary>
        /// Gets or sets a value indicating whether to automatically attempt 
        /// connection on startup if settings are saved.
        /// </summary>
        [ObservableProperty]
        private bool _autoConnect;

        private bool _isSettingsLoading;

        partial void OnRememberConnectionsChanged(bool value)
        {
            SaveCurrentSettings();
        }

        partial void OnAutoConnectChanged(bool value)
        {
            SaveCurrentSettings();
        }

        /// <summary>
        /// Persists the current connection and user settings.
        /// </summary>
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

        /// <summary>
        /// Gets or sets a value indicating whether the target environment 
        /// has been properly initialized for migration.
        /// </summary>
        [ObservableProperty]
        private bool _isEnvironmentReady = true;

        /// <summary>
        /// Gets or sets a value indicating whether the target environment 
        /// initialization is in progress.
        /// </summary>
        [ObservableProperty]
        private bool _isInitializing;

        /// <summary>
        /// Gets or sets the current status message for the environment 
        /// initialization process.
        /// </summary>
        [ObservableProperty]
        private string _initializationStatus = string.Empty;

        private CancellationTokenSource? _sourceCts;
        private CancellationTokenSource? _targetCts;

        /// <summary>
        /// Initializes a new instance of the <see cref="ConnectionViewModel"/> 
        /// class.
        /// </summary>
        /// <param name="navigationService">The navigation service.</param>
        /// <param name="migrationService">The migration service.</param>
        /// <param name="settingsService">The settings service.</param>
        /// <param name="setupService">The environment setup service.</param>
        public ConnectionViewModel(
            INavigationService navigationService,
            IMigrationService migrationService,
            ISettingsService settingsService,
            ISetupService setupService)
        {
            _navigationService = navigationService;
            _migrationService = migrationService;
            _settingsService = settingsService;
            _setupService = setupService;

            LoadSavedSettings();
        }

        /// <summary>
        /// Loads user settings and initiates automatic connections if enabled.
        /// </summary>
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
                        SourceStatus = StatusConnected;
                    }

                    if (_migrationService.TargetProvider != null)
                    {
                        IsTargetConnected = true;
                        TargetStatus = StatusConnected;
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
            SourceStatus = StatusConnecting;

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
                SourceStatus = result ? StatusConnected : StatusFailed;
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
            SourceStatus = StatusCancelled;
        }

        [RelayCommand]
        private void DisconnectSource()
        {
            _migrationService.DisconnectSource();
            IsSourceConnected = false;
            SourceStatus = StatusNotConnected;
        }

        [RelayCommand]
        private async Task TestTargetConnectionAsync()
        {
            _targetCts?.Cancel();
            _targetCts = new CancellationTokenSource();

            IsTargetConnecting = true;
            TargetStatus = StatusConnecting;

            try
            {
                var result = await _migrationService.ConnectTargetAsync(
                    TargetConnectionString,
                    false,
                    _targetCts.Token
                );

                IsTargetConnected = result;
                TargetStatus = result ? StatusConnected : StatusFailed;

                if (result)
                {
                    IsEnvironmentReady = await _setupService
                        .IsEnvironmentReadyAsync(
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
            TargetStatus = StatusCancelled;
        }

        [RelayCommand]
        private void DisconnectTarget()
        {
            _migrationService.DisconnectTarget();
            IsTargetConnected = false;
            TargetStatus = StatusNotConnected;
        }

        [RelayCommand]
        private async Task InitializeEnvironmentAsync()
        {
            if (_migrationService.TargetProvider == null)
            {
                return;
            }

            IsInitializing = true;
            InitializationStatus = StatusInitializing;

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

                await _setupService.DeployPluginAsync(
                    _migrationService.TargetProvider,
                    progress
                );

                IsEnvironmentReady = true;
                InitializationStatus = StatusInitSuccess;
                MessageBox.Show(
                    StatusInitSuccess,
                    "Success",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
            catch (Exception ex)
            {
                InitializationStatus = $"{StatusInitFailedPrefix}{ex.Message}";
                MessageBox.Show(
                    $"{StatusInitFailedPrefix}{ex.Message}",
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

        private bool CanProceed()
        {
            return IsSourceConnected && IsTargetConnected;
        }
    }
}
