namespace dvmig.Core.Metadata
{
    public static class EntityMetadataHelper
    {
        private static readonly string[] StandardEntities =
        {
            "account",
            "activitypointer",
            "appointment",
            "bookableresource",
            "bookableresourcebooking",
            "businessunit",
            "campaign",
            "category",
            "competitor",
            "contact",
            "email",
            "entitlement",
            "equipment",
            "goal",
            "incident",
            "invoice",
            "kbarticle",
            "knowledgearticle",
            "lead",
            "list",
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
            "queue",
            "quote",
            "salesorder",
            "sla",
            "subject",
            "systemuser",
            "task",
            "team"
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