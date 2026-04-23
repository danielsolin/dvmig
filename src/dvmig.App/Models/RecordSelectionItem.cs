using CommunityToolkit.Mvvm.ComponentModel;

namespace dvmig.App.Models
{
    public partial class RecordSelectionItem : ObservableObject
    {
        [ObservableProperty]
        private bool _isSelected;

        public Guid Id { get; }
        public string Name { get; }

        public RecordSelectionItem(Guid id, string name, bool isSelected)
        {
            Id = id;
            Name = name;
            _isSelected = isSelected;
        }
    }
}
