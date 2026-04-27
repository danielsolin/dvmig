using System;
using dvmig.Core.Shared;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace dvmig.Plugins
{
    /// <summary>
    /// Data Migration Plugin responsible for preserving source environment 
    /// timestamps (CreatedOn and ModifiedOn) during the migration process.
    /// It works by looking up temporary date records in the dm_sourcedate 
    /// entity and applying them to the target entity before it is saved.
    /// </summary>
    public class DMPlugin : IPlugin
    {
        /// <summary>
        /// Main entry point for the plugin execution.
        /// </summary>
        /// <param name="serviceProvider">
        /// The service provider that contains the execution context, 
        /// tracing service, and organization service factory.
        /// </param>
        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider
                .GetService(typeof(IPluginExecutionContext));

            var tracingService = (ITracingService)serviceProvider
                .GetService(typeof(ITracingService));

            var factory = (IOrganizationServiceFactory)serviceProvider
                .GetService(typeof(IOrganizationServiceFactory));

            var service = factory.CreateOrganizationService(context.UserId);

            // We allow depth 2 because SyncEngine might trigger an Update 
            // from within its fix logic.
            if (context.Depth > 2)
            {
                return;
            }

            if (!context.InputParameters.Contains("Target"))
            {
                return;
            }

            if (!(context.InputParameters["Target"] is Entity entity))
            {
                return;
            }

            try
            {
                HandleDatePreservation(
                    context,
                    service,
                    tracingService,
                    entity
                );
            }
            catch (Exception ex)
            {
                tracingService.Trace(
                    "DMPlugin error: {0}",
                    ex.ToString()
                );
            }
        }

        /// <summary>
        /// Orchestrates the date preservation logic by identifying the 
        /// message type and applying the appropriate source dates.
        /// </summary>
        private void HandleDatePreservation(
            IPluginExecutionContext context,
            IOrganizationService service,
            ITracingService tracingService,
            Entity entity
        )
        {
            var messageName = context.MessageName.ToLower();
            var sourceDate = GetSourceDate(
                service,
                entity.Id,
                entity.LogicalName
            );

            if (sourceDate == null)
            {
                return;
            }

            if (messageName == "create")
            {
                ApplyCreateDates(entity, sourceDate);
            }
            else if (messageName == "update")
            {
                ApplyUpdateDates(entity, sourceDate);
            }
        }

        /// <summary>
        /// Retrieves the source date record from the dm_sourcedate entity 
        /// for the specified target entity.
        /// </summary>
        private Entity? GetSourceDate(
            IOrganizationService service,
            Guid entityId,
            string logicalName
        )
        {
            var fetchXml = $@"
                <fetch version='1.0' output-format='xml-platform' 
                       mapping='logical' distinct='false' count='1'>
                  <entity name='{SystemConstants.SourceDate.EntityLogicalName}'>
                    <attribute name='{SystemConstants.SourceDate.PrimaryId}' />
                    <attribute name='{SystemConstants.SourceDate.CreatedDate}' />
                    <attribute name='{SystemConstants.SourceDate.ModifiedDate}' />
                    <filter type='and'>
                      <condition attribute='{SystemConstants.SourceDate.EntityId}' 
                        operator='eq' value='{entityId}' />
                      <condition attribute='{SystemConstants.SourceDate.EntityLogicalNameAttr}' 
                        operator='eq' 
                        value='{logicalName.ToLower()}' />
                    </filter>
                  </entity>
                </fetch>";

            var result = service.RetrieveMultiple(
                new FetchExpression(fetchXml)
            );

            if (result.Entities.Count > 0)
            {
                return result.Entities[0];
            }

            return null;
        }

        /// <summary>
        /// Applies both CreatedOn and ModifiedOn dates from the source 
        /// record to the target entity during a Create operation.
        /// </summary>
        private void ApplyCreateDates(
            Entity entity,
            Entity sourceDate
        )
        {
            if (sourceDate.Contains(SystemConstants.SourceDate.CreatedDate))
            {
                var createdDate =
                    sourceDate[SystemConstants.SourceDate.CreatedDate];

                entity["createdon"] = createdDate;
                entity["overriddencreatedon"] = createdDate;
            }

            if (sourceDate.Contains(SystemConstants.SourceDate.ModifiedDate))
            {
                entity["modifiedon"] =
                    sourceDate[SystemConstants.SourceDate.ModifiedDate];
            }
        }

        /// <summary>
        /// Applies the ModifiedOn date from the source record to the 
        /// target entity during an Update operation.
        /// </summary>
        private void ApplyUpdateDates(
            Entity entity,
            Entity sourceDate
        )
        {
            if (sourceDate.Contains(SystemConstants.SourceDate.ModifiedDate))
            {
                entity["modifiedon"] =
                    sourceDate[SystemConstants.SourceDate.ModifiedDate];
            }
        }
    }
}
