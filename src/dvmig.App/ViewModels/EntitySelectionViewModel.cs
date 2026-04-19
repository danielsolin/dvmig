using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using dvmig.App.Models;
using dvmig.App.Services;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace dvmig.App.ViewModels
{
    public partial class EntitySelectionViewModel : ViewModelBase
    {
        private readonly INavigationService _navigationService;
        private readonly IMigrationService _migrationService;

        [ObservableProperty]
        private string _searchText = string.Empty;

        public ObservableCollection<EntitySelectionItem> Entities { get; } = 
            new ObservableCollection<EntitySelectionItem>();

        public EntitySelectionViewModel(
            INavigationService navigationService,
            IMigrationService migrationService)
        {
            _navigationService = navigationService;
            _migrationService = migrationService;
            
            // Initial load (simulation for now)
            LoadEntitiesAsync();
        }

        private async Task LoadEntitiesAsync()
        {
            // Logic to fetch from _migrationService.SourceProvider
            // simulation
            await Task.Delay(500);
            Entities.Add(new EntitySelectionItem("account", "Account") { IsSelected = true });
            Entities.Add(new EntitySelectionItem("contact", "Contact") { IsSelected = true });
            Entities.Add(new EntitySelectionItem("opportunity", "Opportunity"));
            Entities.Add(new EntitySelectionItem("lead", "Lead"));
        }

        [RelayCommand]
        private void StartMigration()
        {
            _navigationService.NavigateTo<MigrationDashboardViewModel>();
        }
    }
}
