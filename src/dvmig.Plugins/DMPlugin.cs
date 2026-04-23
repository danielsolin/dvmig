using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;

namespace dvmig.Plugins
{
    // PreOperation - Synchronous - Create/Update of Any
    public class DMPlugin : IPlugin
    {
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

            var messageName = context.MessageName.ToLower();

            try
            {
                var fetchXml = $@"
                    <fetch version='1.0' output-format='xml-platform' 
                           mapping='logical' distinct='false' count='1'>
                      <entity name='dm_sourcedate'>
                        <attribute name='dm_sourcedateid' />
                        <attribute name='dm_sourcecreateddate' />
                        <attribute name='dm_sourcemodifieddate' />
                        <filter type='and'>
                          <condition attribute='dm_sourceentityid' 
                            operator='eq' value='{entity.Id}' />
                          <condition attribute='dm_sourceentitylogicalname' 
                            operator='eq' value='{entity.LogicalName.ToLower()}' />
                        </filter>
                      </entity>
                    </fetch>";

                var result = service.RetrieveMultiple(
                    new FetchExpression(fetchXml)
                );

                if (result.Entities.Count > 0)
                {
                    var sourceDate = result.Entities[0];
                    
                    if (messageName == "create")
                    {
                        if (sourceDate.Contains("dm_sourcecreateddate"))
                        {
                            var createdDate = 
                                sourceDate["dm_sourcecreateddate"];
                            
                            entity["createdon"] = createdDate;
                            entity["overriddencreatedon"] = createdDate;
                        }

                        if (sourceDate.Contains("dm_sourcemodifieddate"))
                        {
                            entity["modifiedon"] = 
                                sourceDate["dm_sourcemodifieddate"];
                        }
                    }
                    else if (messageName == "update")
                    {
                        if (sourceDate.Contains("dm_sourcemodifieddate"))
                        {
                            entity["modifiedon"] = 
                                sourceDate["dm_sourcemodifieddate"];
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                tracingService.Trace("DMPlugin error: {0}", ex.ToString());
            }
        }
    }
}