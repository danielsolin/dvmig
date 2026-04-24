using System;
using System.Linq;

namespace dvmig.Core.Metadata
{
    public static class EntityMetadataHelper
    {
        private static readonly string[] StandardEntities =
        {
            "account",
            "activitypointer",
            "appointment",
            "assettable",
            "bookableresource",
            "bookableresourcebooking",
            "businessunit",
            "campaign",
            "category",
            "competitor",
            "contact",
            "custtable",
            "email",
            "entitlement",
            "equipment",
            "goal",
            "incident",
            "inventlocation",
            "inventsite",
            "inventtable",
            "invoice",
            "kbarticle",
            "knowledgearticle",
            "lead",
            "list",
            "mainaccount",
            "msdyn_agreement",
            "msdyn_customerasset",
            "msdyn_expense",
            "msdyn_project",
            "msdyn_projecttask",
            "msdyn_timeentry",
            "msdyn_workorder",
            "msdyncrm_customerjourney",
            "msdyncrm_marketingemail",
            "msdyncrm_segment",
            "msevtmgt_event",
            "opportunity",
            "phonecall",
            "pricelevel",
            "product",
            "purchtable",
            "queue",
            "quote",
            "salesorder",
            "salestable",
            "sla",
            "subject",
            "systemuser",
            "task",
            "team",
            "vendtable"
        };

        public static bool IsStandardEntity(string logicalName)
        {
            if (string.IsNullOrWhiteSpace(logicalName))
            {
                return false;
            }

            return StandardEntities.Contains(logicalName.ToLowerInvariant());
        }
    }
}
