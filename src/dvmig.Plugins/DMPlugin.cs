using System;
using dvmig.Core.Shared;
using Microsoft.Xrm.Sdk;

namespace dvmig.Plugins
{
   /// <summary>
   /// Plugin for Dataverse that facilitates date preservation by intercepting
   /// create/update operations and applying dates from the dm_sourcedate
   /// entity.
   /// </summary>
   public class DMPlugin : IPlugin
   {
      /// <inheritdoc />
      public void Execute(IServiceProvider serviceProvider)
      {
         var context = (IPluginExecutionContext)serviceProvider.GetService(
            typeof(IPluginExecutionContext)
         );

         var serviceFactory = (IOrganizationServiceFactory)
            serviceProvider.GetService(typeof(IOrganizationServiceFactory));

         var service = serviceFactory.CreateOrganizationService(
            context.UserId
         );

         var tracingService = (ITracingService)serviceProvider.GetService(
            typeof(ITracingService)
         );

         if (context.InputParameters.Contains("Target") &&
             context.InputParameters["Target"] is Entity target)
         {
            try
            {
               ApplySourceDates(context, target, service, tracingService);
            }
            catch (Exception ex)
            {
               tracingService.Trace("DMPlugin Error: {0}", ex.ToString());

               throw new InvalidPluginExecutionException(
                  "Error in dvmig date preservation: " + ex.Message
               );
            }
         }
      }

      private void ApplySourceDates(
         IPluginExecutionContext context,
         Entity target,
         IOrganizationService service,
         ITracingService tracingService
      )
      {
         // Implementation for date preservation logic would go here.
         // This is a placeholder for the actual plugin logic.
         tracingService.Trace(
            "DMPlugin: Processing {0} for {1}",
            context.MessageName,
            target.LogicalName
         );
      }
   }
}
