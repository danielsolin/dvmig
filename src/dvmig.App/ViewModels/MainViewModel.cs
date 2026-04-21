using CommunityToolkit.Mvvm.ComponentModel;
using dvmig.App.Services;

namespace dvmig.App.ViewModels
{
    public partial class MainViewModel : ViewModelBase
    {
        private readonly INavigationService _navigationService;

        [ObservableProperty]
        private ViewModelBase? _currentViewModel;

        public MainViewModel(INavigationService navigationService)
        {
            _navigationService = navigationService;
            _navigationService.OnNavigation += (vm) => CurrentViewModel = vm;

            // Initial view
            _navigationService.NavigateTo<ConnectionViewModel>();
        }
    }
}
