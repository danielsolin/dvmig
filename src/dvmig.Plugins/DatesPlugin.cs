using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;

namespace dvmig.Plugins
{
    // PreOperation - Synchronous - Create of Any
    public class DatesPlugin : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider
                .GetService(typeof(IPluginExecutionContext));
            
            var factory = (IOrganizationServiceFactory)serviceProvider
                .GetService(typeof(IOrganizationServiceFactory));
            
            var service = factory.CreateOrganizationService(context.UserId);

            if (context.Depth > 1)
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

            var fetchXml = $@"
                <fetch version='1.0' output-format='xml-platform' 
                       mapping='logical' distinct='false' count='1'>
                  <entity name='dm_sourcedate'>
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

            var result = service.RetrieveMultiple(new FetchExpression(fetchXml));

            if (result.Entities.Count > 0)
            {
                var sourceDate = result.Entities[0];

                if (sourceDate.Contains("dm_sourcecreateddate"))
                {
                    entity["createdon"] = sourceDate["dm_sourcecreateddate"];
                    entity["overriddencreatedon"] = DateTime.UtcNow;
                }

                if (sourceDate.Contains("dm_sourcemodifieddate"))
                {
                    entity["modifiedon"] = sourceDate["dm_sourcemodifieddate"];
                }
            }

            context.InputParameters["Target"] = entity;
        }
    }
}
