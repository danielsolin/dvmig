using CommunityToolkit.Mvvm.ComponentModel;

namespace dvmig.App.ViewModels
{
   /// <summary>
   /// Base class for all view models in the application, providing common 
   /// functionality such as property change notification and status reporting.
   /// </summary>
   public abstract partial class ViewModelBase : ObservableObject
   {
      [ObservableProperty]
      private string _statusText = "Ready";
   }
}
