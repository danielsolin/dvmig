using System;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;

using XrmToolBox.Extensibility;

using dvmig.Core.Interfaces;
using dvmig.Core.Provisioning;
using dvmig.Core.Synchronization;

namespace dvmig.XTB.Shared
{
   public static class DIConfigurator
   {
      public static IServiceProvider CreateServiceProvider(
         PluginControlBase control,
         RichTextBox logControl
      )
      {
         var services = new ServiceCollection();

         // Shared Infrastructure
         var logger = new XTBLogger();
         services.AddSingleton<ILogger>(logger);
         services.AddSingleton<ISyncStateService, SyncStateService>();
         services.AddSingleton<ISettingsService>(
            new XTBSettingsService(control)
         );

         // Metadata & Synchronization
         services.AddTransient<IEntityService, EntityService>();
         services.AddTransient<IUserService, UserService>();
         services.AddTransient<IWipeDataService, WipeDataService>();
         services.AddTransient<IEnvironmentService, EnvironmentService>();

         var serviceProvider = services.BuildServiceProvider();

         // Attach UI log update
         var progress = new Progress<string>(msg =>
         {
            if(logControl.InvokeRequired)
            {
               logControl.Invoke(new Action(() =>
                  {
                     XTBLogWriter.AppendLogMessage(logControl, msg);
                  }));
            }
            else
            {
               XTBLogWriter.AppendLogMessage(logControl, msg);
            }
         });
         logger.AttachProgress(progress);

         return serviceProvider;
      }
   }
}
