using dvmig.App.ViewModels;

namespace dvmig.App.Services
{
    /// <summary>
    /// Service interface for handling navigation between different view 
    /// models in the application.
    /// </summary>
    public interface INavigationService
    {
        /// <summary>
        /// Navigates to the specified view model type.
        /// </summary>
        /// <typeparam name="TViewModel">
        /// The type of the view model to navigate to.
        /// </typeparam>
        void NavigateTo<TViewModel>() where TViewModel : ViewModelBase;

        /// <summary>
        /// Event that is raised when navigation occurs.
        /// </summary>
        event Action<ViewModelBase>? OnNavigation;
    }

    /// <summary>
    /// Implementation of the navigation service using a view model factory.
    /// </summary>
    public class NavigationService : INavigationService
    {
        private readonly Func<Type, ViewModelBase> _viewModelFactory;

        /// <inheritdoc />
        public event Action<ViewModelBase>? OnNavigation;

        /// <summary>
        /// Initializes a new instance of the 
        /// <see cref="NavigationService"/> class.
        /// </summary>
        /// <param name="viewModelFactory">
        /// A factory function that creates view model instances by type.
        /// </param>
        public NavigationService(Func<Type, ViewModelBase> viewModelFactory)
        {
            _viewModelFactory = viewModelFactory;
        }

        /// <inheritdoc />
        public void NavigateTo<TViewModel>() where TViewModel : ViewModelBase
        {
            var viewModel = _viewModelFactory(typeof(TViewModel));
            OnNavigation?.Invoke(viewModel);
        }
    }
}
