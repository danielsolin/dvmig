using CommunityToolkit.Mvvm.ComponentModel;

namespace dvmig.App.Models
{
    public partial class EntitySelectionItem : ObservableObject
    {
        [ObservableProperty]
        private bool _isSelected;

        public string LogicalName { get; }
        public string DisplayName { get; }
        public long RecordCount { get; set; }

        public EntitySelectionItem(string logicalName, string displayName)
        {
            LogicalName = logicalName;
            DisplayName = displayName;
        }
    }
}
