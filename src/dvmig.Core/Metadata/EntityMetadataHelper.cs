namespace dvmig.Core.Metadata
{
    /// <summary>
    /// Provides helper methods for identifying and working with Dataverse 
    /// entity metadata.
    /// </summary>
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

        /// <summary>
        /// Determines whether the specified entity logical name refers to a 
        /// standard (out-of-the-box) Dataverse entity.
        /// </summary>
        /// <param name="logicalName">
        /// The logical name of the entity to check.
        /// </param>
        /// <returns>
        /// True if the entity is a standard entity; otherwise, false.
        /// </returns>
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