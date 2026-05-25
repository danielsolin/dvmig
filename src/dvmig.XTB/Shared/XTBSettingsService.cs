using XrmToolBox.Extensibility;

using dvmig.Core.Interfaces;
using dvmig.Core.Settings;
using System;

namespace dvmig.XTB.Shared
{
   /// <summary>
   /// Implementation of <see cref="ISettingsService"/> that uses the 
   /// XrmToolBox <see cref="SettingsManager"/>.
   /// </summary>
   public class XTBSettingsService : ISettingsService
   {
      private readonly Type _settingsType;

      public XTBSettingsService(PluginControlBase control)
      {
         _settingsType = control.GetType();
      }
      
      /// <inheritdoc />
      public UserSettings LoadSettings()
      {
         UserSettings settings;
         
         if (SettingsManager.Instance.TryLoad(
               _settingsType.GetType(),
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
         SettingsManager.Instance.Save(_settingsType.GetType(), settings);
      }
   }
}
