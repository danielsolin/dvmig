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
    public partial class EntitySelectionViewModel : ViewModelBase
    {
        private readonly INavigationService _navigationService;
        private readonly IMigrationService _migrationService;
        private readonly ICollectionView _entitiesView;

        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private bool _showSystemEntities;

        [ObservableProperty]
        private bool _isLoading;

        public ObservableCollection<EntitySelectionItem> Entities { get; } =
            new ObservableCollection<EntitySelectionItem>();

        public ICollectionView SelectedEntitiesView { get; }

        public EntitySelectionViewModel(
            INavigationService navigationService,
            IMigrationService migrationService)
        {
            _navigationService = navigationService;
            _migrationService = migrationService;

            _entitiesView = CollectionViewSource.GetDefaultView(Entities);
            _entitiesView.Filter = FilterEntities;

            SelectedEntitiesView = new ListCollectionView(Entities);
            SelectedEntitiesView.Filter = FilterSelectedEntities;

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

                        if (_migrationService.SelectedEntities.Contains(
                            meta.LogicalName))
                        {
                            item.IsSelected = true;
                        }

                        item.PropertyChanged += OnItemPropertyChanged;
                        Entities.Add(item);
                    }

                    StartMigrationCommand.NotifyCanExecuteChanged();
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

            // Mutual exclusivity: only show if NOT selected
            if (item.IsSelected)
            {
                return false;
            }

            // Filter out system/custom entities (contain underscore) 
            // if toggle is off
            if (!ShowSystemEntities)
            {
                if (item.LogicalName.Contains("_"))
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

        private bool FilterSelectedEntities(object obj)
        {
            if (obj is not EntitySelectionItem item)
            {
                return false;
            }

            return item.IsSelected;
        }

        partial void OnSearchTextChanged(string value)
        {
            _entitiesView.Refresh();
        }

        partial void OnShowSystemEntitiesChanged(bool value)
        {
            _entitiesView.Refresh();
        }

        [RelayCommand]
        private void ToggleSelection(EntitySelectionItem item)
        {
            item.IsSelected = !item.IsSelected;
        }

        [RelayCommand(CanExecute = nameof(CanStartMigration))]
        private void StartMigration()
        {
            _migrationService.SelectedEntities.Clear();
            _migrationService.SelectedEntities.AddRange(
                Entities
                    .Where(e => e.IsSelected)
                    .Select(e => e.LogicalName)
            );

            _navigationService.NavigateTo<MigrationDashboardViewModel>();
        }

        private bool CanStartMigration()
        {
            return Entities.Any(e => e.IsSelected);
        }

        private void OnItemPropertyChanged(
            object? sender,
            PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(EntitySelectionItem.IsSelected))
            {
                _entitiesView.Refresh();
                SelectedEntitiesView.Refresh();
                StartMigrationCommand.NotifyCanExecuteChanged();
            }
        }

        [RelayCommand]
        private void GoBack()
        {
            _migrationService.SelectedEntities.Clear();
            _migrationService.SelectedEntities.AddRange(
                Entities
                    .Where(e => e.IsSelected)
                    .Select(e => e.LogicalName)
            );

            _navigationService.NavigateTo<ConnectionViewModel>();
        }
    }
}
