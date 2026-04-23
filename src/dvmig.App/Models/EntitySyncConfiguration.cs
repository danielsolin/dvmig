namespace dvmig.App.Models
{
    public class EntitySyncConfiguration
    {
        public string LogicalName { get; }
        public bool SyncAllRecords { get; set; } = true;
        public HashSet<Guid> SelectedRecordIds { get; } = new();

        public EntitySyncConfiguration(string logicalName)
        {
            LogicalName = logicalName;
        }
    }
}
