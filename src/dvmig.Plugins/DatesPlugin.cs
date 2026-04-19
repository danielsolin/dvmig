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
                  <entity name='mp_sourcedate'>
                    <attribute name='mp_sourcecreateddate' />
                    <attribute name='mp_sourcemodifieddate' />
                    <filter type='and'>
                      <condition attribute='mp_sourceentityid' 
                                 operator='eq' value='{entity.Id}' />
                      <condition attribute='mp_sourceentitylogicalname' 
                                 operator='eq' value='{entity.LogicalName.ToLower()}' />
                    </filter>
                  </entity>
                </fetch>";

            var result = service.RetrieveMultiple(new FetchExpression(fetchXml));

            if (result.Entities.Count > 0)
            {
                var sourceDate = result.Entities[0];

                if (sourceDate.Contains("mp_sourcecreateddate"))
                {
                    entity["createdon"] = sourceDate["mp_sourcecreateddate"];
                    entity["overriddencreatedon"] = DateTime.UtcNow;
                }

                if (sourceDate.Contains("mp_sourcemodifieddate"))
                {
                    entity["modifiedon"] = sourceDate["mp_sourcemodifieddate"];
                }
            }

            context.InputParameters["Target"] = entity;
        }
    }
}
