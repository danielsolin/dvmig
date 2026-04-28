using CommunityToolkit.Mvvm.ComponentModel;
using dvmig.App.Services;

namespace dvmig.App.ViewModels
{
   /// <summary>
   /// The main view model for the application, responsible for handling
   /// top-level navigation and hosting the current view's view model.
   /// </summary>
   public partial class MainViewModel : ViewModelBase
   {
      private readonly INavigationService _navigationService;

      [ObservableProperty]
      private ViewModelBase? _currentViewModel;

      /// <summary>
      /// Initializes a new instance of the <see cref="MainViewModel"/> class.
      /// </summary>
      /// <param name="navigationService">The navigation service.</param>
      public MainViewModel(INavigationService navigationService)
      {
         _navigationService = navigationService;
         _navigationService.OnNavigation += (vm) => CurrentViewModel = vm;

         // Initial view
         _navigationService.NavigateTo<ConnectionViewModel>();
      }
   }
}
