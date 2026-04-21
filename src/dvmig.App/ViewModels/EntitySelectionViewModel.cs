using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using dvmig.App.Models;
using dvmig.App.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;

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
        private bool _isLoading;

        public ObservableCollection<EntitySelectionItem> Entities { get; } =
            new ObservableCollection<EntitySelectionItem>();

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
                        var displayName = meta.DisplayName?.UserLocalizedLabel?.Label
                                          ?? meta.LogicalName;

                        Entities.Add(new EntitySelectionItem(meta.LogicalName, displayName));
                    }
                });
            }
            catch (System.Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show(
                        $"Failed to load metadata: {ex.Message}",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
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

            if (string.IsNullOrWhiteSpace(SearchText))
            {
                return true;
            }

            return item.DisplayName.Contains(SearchText, System.StringComparison.OrdinalIgnoreCase) ||
                   item.LogicalName.Contains(SearchText, System.StringComparison.OrdinalIgnoreCase);
        }

        partial void OnSearchTextChanged(string value)
        {
            _entitiesView.Refresh();
        }

        [RelayCommand]
        private void StartMigration()
        {
            _migrationService.SelectedEntities.Clear();
            _migrationService.SelectedEntities.AddRange(
                Entities.Where(e => e.IsSelected).Select(e => e.LogicalName));

            _navigationService.NavigateTo<MigrationDashboardViewModel>();
        }
    }
}
