using CommunityToolkit.Mvvm.ComponentModel;

namespace dvmig.App.ViewModels
{
    public abstract partial class ViewModelBase : ObservableObject
    {
        [ObservableProperty]
        private string _statusText = "Ready";
    }
}
