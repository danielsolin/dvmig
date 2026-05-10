using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace dvmig.Plugins
{
   /// <summary>
   /// Plugin responsible for preserving source environment
   /// audit timestamps during the migration.
   /// </summary>
   public class DMPlugin : IPlugin
   {
      private const string SourceDataEntityLogicalName = "dm_sourcedata";
      private const string SourceDataPrimaryId = "dm_sourcedataid";
      private const string SourceDataEntityId = "dm_sourceentityid";
      private const string SourceDataEntityLogicalNameAttr = 
         "dm_sourceentitylogicalname";
      private const string SourceDataCreatedOn = "dm_sourcecreateddate";
      private const string SourceDataModifiedOn = "dm_sourcemodifieddate";

      private const string CreatedOnAttr = "createdon";
      private const string ModifiedOnAttr = "modifiedon";
      private const string OverriddenCreatedOnAttr = "overriddencreatedon";

      public void Execute(IServiceProvider serviceProvider)
      {
         var context = (IPluginExecutionContext)serviceProvider
            .GetService(typeof(IPluginExecutionContext));

         var tracingService = (ITracingService)serviceProvider
            .GetService(typeof(ITracingService));

         var factory = (IOrganizationServiceFactory)serviceProvider
            .GetService(typeof(IOrganizationServiceFactory));

         // Run as SYSTEM (null) to ensure we can always read dm_sourcedata
         var service = factory.CreateOrganizationService(null);

         if (context.Depth > 2)
            return;

         if (!context.InputParameters.Contains("Target") ||
             !(context.InputParameters["Target"] is Entity entity))
            return;

         try
         {
            HandleAuditDataPreservation(
               context,
               service,
               tracingService,
               entity
            );
         }
         catch(Exception ex)
         {
            tracingService.Trace("DMPlugin error: {0}", ex.ToString());
         }
      }

      private void HandleAuditDataPreservation(
         IPluginExecutionContext context,
         IOrganizationService service,
         ITracingService tracingService,
         Entity entity
      )
      {
         var messageName = context.MessageName.ToLower();

         tracingService.Trace(
            "Handling {0} for {1}:{2}",
            messageName,
            entity.LogicalName,
            entity.Id
         );

         var sourceData = GetSourceData(
            service,
            entity.Id,
            entity.LogicalName
         );

         if (sourceData == null)
         {
            tracingService.Trace(
               "No source data found for {0}:{1}. Skipping timestamps.",
               entity.LogicalName,
               entity.Id
            );

            return;
         }

         tracingService.Trace("Applying timestamps from source data...");

         if (messageName == "create")
            ApplyCreateTimestamps(entity, sourceData, tracingService);
         else if (messageName == "update")
            ApplyUpdateTimestamps(entity, sourceData, tracingService);
      }

      private Entity? GetSourceData(
         IOrganizationService service,
         Guid entityId,
         string logicalName
      )
      {
         var entityName = SourceDataEntityLogicalName;
         var primaryId = SourceDataPrimaryId;
         var createdOnAttr = SourceDataCreatedOn;
         var modifiedOnAttr = SourceDataModifiedOn;
         var sourceEntityId = SourceDataEntityId;
         var logicalNameAttr = SourceDataEntityLogicalNameAttr;

         var fetchXml =
            $@"<fetch version='1.0' output-format='xml-platform'
                     mapping='logical' distinct='false' count='1'>
               <entity name='{entityName}'>
                  <attribute name='{primaryId}' />
                  <attribute name='{createdOnAttr}' />
                  <attribute name='{modifiedOnAttr}' />
                  <filter type='and'>
                     <condition attribute='{sourceEntityId}'
                     operator='eq' value='{entityId}' />
                     <condition attribute='{logicalNameAttr}'
                     operator='eq'
                     value='{logicalName.ToLower()}' />
                  </filter>
               </entity>
               </fetch>";

         var result = service.RetrieveMultiple(
            new FetchExpression(fetchXml)
         );

         return result.Entities.Count > 0 ? result.Entities[0] : null;
      }

      private void ApplyCreateTimestamps(
         Entity entity,
         Entity sourceData,
         ITracingService tracingService
      )
      {
         if (sourceData.Contains(SourceDataCreatedOn))
         {
            var createdDate = sourceData[SourceDataCreatedOn];

            tracingService.Trace("Setting CreatedOn to {0}", createdDate);

            entity[CreatedOnAttr] = createdDate;
            entity[OverriddenCreatedOnAttr] =
               createdDate;
         }

         if (sourceData.Contains(SourceDataModifiedOn))
         {
            tracingService.Trace(
               "Setting ModifiedOn to {0}",
               sourceData[SourceDataModifiedOn]
            );

            entity[ModifiedOnAttr] =
               sourceData[SourceDataModifiedOn];
         }
      }

      private void ApplyUpdateTimestamps(
         Entity entity,
         Entity sourceData,
         ITracingService tracingService
      )
      {
         if (sourceData.Contains(SourceDataModifiedOn))
         {
            tracingService.Trace(
               "Setting ModifiedOn to {0}",
               sourceData[SourceDataModifiedOn]
            );

            entity[ModifiedOnAttr] =
               sourceData[SourceDataModifiedOn];
         }
      }
   }
}

