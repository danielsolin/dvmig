using CommunityToolkit.Mvvm.ComponentModel;

namespace dvmig.App.Models
{
    public partial class EntitySelectionItem : ObservableObject
    {
        [ObservableProperty]
        private bool _isSelected;

        [ObservableProperty]
        private bool _syncAllRecords = true;

        public string LogicalName { get; }
        public string DisplayName { get; }
        public long RecordCount { get; set; }
        public bool IsRecordCountFetched { get; set; }
        public bool IsFetchingRecordCount { get; set; }
        public HashSet<Guid> SelectedRecordIds { get; } = new();

        public EntitySelectionItem(string logicalName, string displayName)
        {
            LogicalName = logicalName;
            DisplayName = displayName;
        }
    }
}
