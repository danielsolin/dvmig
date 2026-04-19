using dvmig.App.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace dvmig.App.Services
{
    public interface INavigationService
    {
        void NavigateTo<TViewModel>() where TViewModel : ViewModelBase;
        event Action<ViewModelBase>? OnNavigation;
    }

    public class NavigationService : INavigationService
    {
        private readonly Func<Type, ViewModelBase> _viewModelFactory;
        public event Action<ViewModelBase>? OnNavigation;

        public NavigationService(Func<Type, ViewModelBase> viewModelFactory)
        {
            _viewModelFactory = viewModelFactory;
        }

        public void NavigateTo<TViewModel>() where TViewModel : ViewModelBase
        {
            var viewModel = _viewModelFactory(typeof(TViewModel));
            OnNavigation?.Invoke(viewModel);
        }
    }
}
