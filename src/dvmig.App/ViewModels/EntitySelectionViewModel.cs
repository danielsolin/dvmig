using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using dvmig.App.Models;
using dvmig.App.Services;

namespace dvmig.App.ViewModels
{
    /// <summary>
    /// View model for the entity and record selection screen. Allows users to
    /// choose which entities and specific records to migrate.
    /// </summary>
    public partial class EntitySelectionViewModel : ViewModelBase
    {
        private readonly INavigationService _navigationService;
        private readonly IMigrationService _migrationService;
        private readonly ICollectionView _entitiesView;
        private CancellationTokenSource? _recordCts;

        /// <summary>
        /// Gets or sets the search text used to filter the list of entities.
        /// </summary>
        [ObservableProperty]
        private string _searchText = string.Empty;

        /// <summary>
        /// Gets or sets a value indicating whether system/internal entities 
        /// should be displayed in the list.
        /// </summary>
        [ObservableProperty]
        private bool _showSystemEntities;

        /// <summary>
        /// Gets or sets a value indicating whether the entity metadata is 
        /// currently being loaded.
        /// </summary>
        [ObservableProperty]
        private bool _isLoading;

        /// <summary>
        /// Gets or sets the currently active (highlighted) entity for 
        /// record selection.
        /// </summary>
        [ObservableProperty]
        private EntitySelectionItem? _activeEntity;

        /// <summary>
        /// Gets or sets the search text used to filter the list of records 
        /// for the active entity.
        /// </summary>
        [ObservableProperty]
        private string _recordSearchText = string.Empty;

        /// <summary>
        /// Gets or sets a value indicating whether records for the active 
        /// entity are currently being loaded.
        /// </summary>
        [ObservableProperty]
        private bool _isLoadingRecords;

        /// <summary>
        /// Gets the collection of available entities for selection.
        /// </summary>
        public ObservableCollection<EntitySelectionItem> Entities { get; } =
            new ObservableCollection<EntitySelectionItem>();

        /// <summary>
        /// Gets the collection of records for the currently active entity.
        /// </summary>
        public ObservableCollection<RecordSelectionItem> Records { get; } =
            new ObservableCollection<RecordSelectionItem>();

        /// <summary>
        /// Initializes a new instance of the <see cref="EntitySelectionViewModel"/> 
        /// class.
        /// </summary>
        /// <param name="navigationService">The navigation service.</param>
        /// <param name="migrationService">The migration service.</param>
        public EntitySelectionViewModel(
            INavigationService navigationService,
            IMigrationService migrationService)
        {
            _navigationService = navigationService;
            _migrationService = migrationService;

            _entitiesView = CollectionViewSource.GetDefaultView(Entities);
            _entitiesView.Filter = FilterEntities;

            // Load on start
            _ = LoadEntitiesAsync();
        }

        private async Task LoadEntitiesAsync()
        {
            IsLoading = true;
            try
            {
                var metadata = await _migrationService.GetSourceEntitiesAsync();

                // Ensure UI thread when updating collection
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Entities.Clear();
                    foreach (var meta in metadata)
                    {
                        var displayName =
                            meta.DisplayName?.UserLocalizedLabel?.Label ??
                            meta.LogicalName;

                        var item = new EntitySelectionItem(
                            meta.LogicalName,
                            displayName
                        );

                        var existing = _migrationService.SelectedEntities
                            .FirstOrDefault(e => e.LogicalName == meta.LogicalName);

                        if (existing != null)
                        {
                            item.IsSelected = true;
                            item.SyncAllRecords = existing.SyncAllRecords;
                            foreach (var id in existing.SelectedRecordIds)
                            {
                                item.SelectedRecordIds.Add(id);
                            }
                        }

                        item.PropertyChanged += OnItemPropertyChanged;
                        Entities.Add(item);
                    }

                    StartMigrationCommand.NotifyCanExecuteChanged();
                    _ = UpdateSyncCountAsync();
                });
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show(
                        $"Failed to load metadata: {ex.Message}",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                });
            }
            finally
            {
                IsLoading = false;
            }
        }

        private bool FilterEntities(object obj)
        {
            if (obj is not EntitySelectionItem item)
            {
                return false;
            }

            // Filter out non-standard entities if toggle is off
            if (!ShowSystemEntities)
            {
                if (!_migrationService.IsStandardEntity(item.LogicalName))
                {
                    return false;
                }
            }

            if (string.IsNullOrWhiteSpace(SearchText))
            {
                return true;
            }

            var search = SearchText;
            var comparison = StringComparison.OrdinalIgnoreCase;

            return item.DisplayName.Contains(search, comparison) ||
                   item.LogicalName.Contains(search, comparison);
        }

        partial void OnSearchTextChanged(string value)
        {
            _entitiesView.Refresh();
        }

        partial void OnShowSystemEntitiesChanged(bool value)
        {
            _entitiesView.Refresh();
        }

        partial void OnActiveEntityChanged(EntitySelectionItem? value)
        {
            SaveConfiguration();
            RecordSearchText = string.Empty;
            _ = LoadRecordsAsync();
            StartMigrationCommand.NotifyCanExecuteChanged();
        }

        partial void OnRecordSearchTextChanged(string value)
        {
            _ = LoadRecordsAsync();
        }

        private async Task LoadRecordsAsync()
        {
            if (ActiveEntity == null)
            {
                Records.Clear();
                return;
            }

            _recordCts?.Cancel();
            _recordCts = new CancellationTokenSource();
            var token = _recordCts.Token;

            IsLoadingRecords = true;
            try
            {
                // Add a small delay for debounce
                await Task.Delay(300, token);

                var records = await _migrationService.GetRecordsAsync(
                    ActiveEntity.LogicalName,
                    RecordSearchText,
                    token
                );

                Application.Current.Dispatcher.Invoke(() =>
                {
                    Records.Clear();
                    foreach (var record in records)
                    {
                        record.PropertyChanged += OnRecordPropertyChanged;
                        Records.Add(record);
                    }

                    // Trigger validation after records are loaded
                    StartMigrationCommand.NotifyCanExecuteChanged();
                });
            }
            catch (OperationCanceledException)
            {
                // Ignore
            }
            finally
            {
                if (!token.IsCancellationRequested)
                {
                    IsLoadingRecords = false;
                }
            }
        }

        private void OnRecordPropertyChanged(
            object? sender,
            PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(RecordSelectionItem.IsSelected) &&
                sender is RecordSelectionItem record &&
                ActiveEntity != null)
            {
                if (record.IsSelected)
                {
                    ActiveEntity.SelectedRecordIds.Add(record.Id);
                    // Automatically select the entity if a record is chosen
                    ActiveEntity.IsSelected = true;
                }
                else
                {
                    ActiveEntity.SelectedRecordIds.Remove(record.Id);
                }

                StartMigrationCommand.NotifyCanExecuteChanged();
                _ = UpdateSyncCountAsync();
            }
        }

        [RelayCommand]
        private void SelectEntity(EntitySelectionItem item)
        {
            ActiveEntity = item;
            item.IsSelected = true;
            StartMigrationCommand.NotifyCanExecuteChanged();
            _ = UpdateSyncCountAsync();
        }

        [RelayCommand]
        private void SelectRecord(RecordSelectionItem record)
        {
            record.IsSelected = true;
            StartMigrationCommand.NotifyCanExecuteChanged();
            _ = UpdateSyncCountAsync();
        }

        [RelayCommand]
        private void ToggleSelection(EntitySelectionItem item)
        {
            item.IsSelected = !item.IsSelected;
        }

        [RelayCommand(CanExecute = nameof(CanStartMigration))]
        private void StartMigration()
        {
            SaveConfiguration();
            _navigationService.NavigateTo<MigrationDashboardViewModel>();
        }

        private void SaveConfiguration()
        {
            foreach (var item in Entities)
            {
                var existing = _migrationService.SelectedEntities
                    .FirstOrDefault(e => e.LogicalName == item.LogicalName);

                if (item.IsSelected)
                {
                    if (existing == null)
                    {
                        existing = new EntitySyncConfiguration(item.LogicalName);
                        _migrationService.SelectedEntities.Add(existing);
                    }

                    existing.SyncAllRecords = item.SyncAllRecords;
                    existing.SelectedRecordIds.Clear();
                    foreach (var id in item.SelectedRecordIds)
                    {
                        existing.SelectedRecordIds.Add(id);
                    }
                }
                else if (existing != null)
                {
                    _migrationService.SelectedEntities.Remove(existing);
                }
            }
        }

        private bool CanStartMigration()
        {
            var selected = Entities.Where(e => e.IsSelected).ToList();

            // Allow proceeding if at least one selected entity is valid.
            // Invalid entities (SyncAll=false and 0 records) will be skipped.
            return selected.Any(e =>
                e.SyncAllRecords || e.SelectedRecordIds.Any()
            );
        }

        private Task UpdateSyncCountAsync()
        {
            var selectedEntities = Entities.Where(e => e.IsSelected).ToList();
            long total = 0;
            bool pendingCount = false;
            int selectedCount = selectedEntities.Count;

            foreach (var item in selectedEntities)
            {
                if (item.SyncAllRecords)
                {
                    if (!item.IsRecordCountFetched)
                    {
                        pendingCount = true;
                        if (!item.IsFetchingRecordCount)
                        {
                            item.IsFetchingRecordCount = true;
                            // Fetch count in background
                            _ = FetchRecordCountBackgroundAsync(item);
                        }
                    }
                    else
                    {
                        total += item.RecordCount;
                    }
                }
                else
                {
                    total += item.SelectedRecordIds.Count;
                }
            }

            if (selectedCount == 0)
            {
                StatusText = "Ready";
            }
            else
            {
                StatusText = pendingCount
                    ? $"Records to sync: {total}+ (fetching counts... {selectedCount} entities)"
                    : $"Records to sync: {total} ({selectedCount} entities selected)";
            }

            return Task.CompletedTask;
        }

        private async Task FetchRecordCountBackgroundAsync(
            EntitySelectionItem item)
        {
            try
            {
                var count = await _migrationService.GetRecordCountAsync(
                    item.LogicalName
                );

                Application.Current.Dispatcher.Invoke(() =>
                {
                    item.RecordCount = count;
                    item.IsRecordCountFetched = true;
                    _ = UpdateSyncCountAsync();
                });
            }
            catch
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    item.RecordCount = 0;
                    item.IsRecordCountFetched = true;
                    _ = UpdateSyncCountAsync();
                });
            }
        }

        private void OnItemPropertyChanged(
            object? sender,
            PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(EntitySelectionItem.IsSelected))
            {
                StartMigrationCommand.NotifyCanExecuteChanged();
                _ = UpdateSyncCountAsync();
            }
            else if (e.PropertyName == nameof(EntitySelectionItem.SyncAllRecords) &&
                     sender is EntitySelectionItem item)
            {
                if (item.SyncAllRecords)
                {
                    // If "Sync All" is toggled on, ensure the entity is selected
                    item.IsSelected = true;
                }

                StartMigrationCommand.NotifyCanExecuteChanged();
                _ = UpdateSyncCountAsync();
            }
        }

        [RelayCommand]
        private void GoBack()
        {
            SaveConfiguration();
            _navigationService.NavigateTo<ConnectionViewModel>();
        }
    }
}
