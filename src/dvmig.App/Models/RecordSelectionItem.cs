using CommunityToolkit.Mvvm.ComponentModel;

namespace dvmig.App.Models
{
   /// <summary>
   /// Represents a single record that can be selected for migration.
   /// </summary>
   public partial class RecordSelectionItem : ObservableObject
   {
      /// <summary>
      /// Gets or sets a value indicating whether this record is selected 
      /// for migration.
      /// </summary>
      [ObservableProperty]
      private bool _isSelected;

      /// <summary>
      /// Gets the unique identifier of the record.
      /// </summary>
      public Guid Id { get; }

      /// <summary>
      /// Gets the primary name or display string of the record.
      /// </summary>
      public string Name { get; }

      /// <summary>
      /// Initializes a new instance of the <see cref="RecordSelectionItem"/> 
      /// class.
      /// </summary>
      /// <param name="id">The record ID.</param>
      /// <param name="name">The display name of the record.</param>
      /// <param name="isSelected">The initial selection state.</param>
      public RecordSelectionItem(Guid id, string name, bool isSelected)
      {
         Id = id;
         Name = name;
         _isSelected = isSelected;
      }
   }
}
