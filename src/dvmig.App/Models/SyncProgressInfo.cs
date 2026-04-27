using CommunityToolkit.Mvvm.ComponentModel;

namespace dvmig.App.Models
{
    /// <summary>
    /// Tracks the progress of a migration operation, including record counts 
    /// and overall percentage.
    /// </summary>
    public partial class SyncProgressInfo : ObservableObject
    {
        /// <summary>
        /// Gets or sets the logical name of the entity currently 
        /// being processed.
        /// </summary>
        [ObservableProperty]
        private string _currentEntity = string.Empty;

        /// <summary>
        /// Gets or sets the total number of records to be migrated.
        /// </summary>
        [ObservableProperty]
        private int _totalRecords;

        /// <summary>
        /// Gets or sets the number of records that have been processed 
        /// so far.
        /// </summary>
        [ObservableProperty]
        private int _processedRecords;

        /// <summary>
        /// Gets or sets the number of successfully migrated records.
        /// </summary>
        [ObservableProperty]
        private int _successCount;

        /// <summary>
        /// Gets or sets the number of records that failed to migrate.
        /// </summary>
        [ObservableProperty]
        private int _failureCount;

        /// <summary>
        /// Gets or sets the calculated progress percentage (0-100).
        /// </summary>
        [ObservableProperty]
        private double _progressPercentage;

        /// <summary>
        /// Updates the progress information and recalculates the percentage.
        /// </summary>
        /// <param name="processed">The number of processed records.</param>
        /// <param name="success">The number of successful records.</param>
        /// <param name="failure">The number of failed records.</param>
        public void Update(
            int processed,
            int success,
            int failure
        )
        {
            ProcessedRecords = processed;
            SuccessCount = success;
            FailureCount = failure;

            if (TotalRecords > 0)
                ProgressPercentage = (double)processed / TotalRecords * 100;
        }
    }
}
