using CommunityToolkit.Mvvm.ComponentModel;

namespace dvmig.App.Models
{
    /// <summary>
    /// Represents an entity that can be selected for migration, including 
    /// its selection state and record counts.
    /// </summary>
    public partial class EntitySelectionItem : ObservableObject
    {
        /// <summary>
        /// Gets or sets a value indicating whether this entity is selected 
        /// for migration.
        /// </summary>
        [ObservableProperty]
        private bool _isSelected;

        /// <summary>
        /// Gets or sets a value indicating whether all records of this 
        /// entity should be migrated, or only a specific subset.
        /// </summary>
        [ObservableProperty]
        private bool _syncAllRecords = true;

        /// <summary>
        /// Gets the logical name of the entity.
        /// </summary>
        public string LogicalName { get; }

        /// <summary>
        /// Gets the display name of the entity.
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        /// Gets or sets the total record count for this entity in the 
        /// source environment.
        /// </summary>
        public long RecordCount { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the record count has 
        /// been successfully retrieved.
        /// </summary>
        public bool IsRecordCountFetched { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the record count is 
        /// currently being fetched.
        /// </summary>
        public bool IsFetchingRecordCount { get; set; }

        /// <summary>
        /// Gets the collection of specific record IDs selected for migration 
        /// when <see cref="SyncAllRecords"/> is false.
        /// </summary>
        public HashSet<Guid> SelectedRecordIds { get; } = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="EntitySelectionItem"/> 
        /// class.
        /// </summary>
        /// <param name="logicalName">The logical name of the entity.</param>
        /// <param name="displayName">The display name of the entity.</param>
        public EntitySelectionItem(string logicalName, string displayName)
        {
            LogicalName = logicalName;
            DisplayName = displayName;
        }
    }
}
