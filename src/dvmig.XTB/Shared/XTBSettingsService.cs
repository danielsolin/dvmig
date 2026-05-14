using XrmToolBox.Extensibility;

using dvmig.Core.Interfaces;
using dvmig.Core.Settings;

namespace dvmig.XTB.Shared
{
   /// <summary>
   /// Implementation of <see cref="ISettingsService"/> that uses the 
   /// XrmToolBox <see cref="SettingsManager"/>.
   /// </summary>
   public class XTBSettingsService : ISettingsService
   {
      private readonly PluginControlBase _control;

      public XTBSettingsService(PluginControlBase control)
      {
         _control = control;
      }
      
      /// <inheritdoc />
      public UserSettings LoadSettings()
      {
         UserSettings settings;
         
         if (SettingsManager.Instance.TryLoad(
               _control.GetType(),
               out settings
            )
         )
         {
            return settings;
         }

         return new UserSettings();
      }

      /// <inheritdoc />
      public void SaveSettings(UserSettings settings)
      {
         SettingsManager.Instance.Save(_control.GetType(), settings);
      }
   }
}
