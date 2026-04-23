using CommunityToolkit.Mvvm.ComponentModel;

namespace dvmig.App.Models
{
    public partial class SyncProgressInfo : ObservableObject
    {
        [ObservableProperty]
        private string _currentEntity = string.Empty;

        [ObservableProperty]
        private int _totalRecords;

        [ObservableProperty]
        private int _processedRecords;

        [ObservableProperty]
        private int _successCount;

        [ObservableProperty]
        private int _failureCount;

        [ObservableProperty]
        private double _progressPercentage;

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
            {
                ProgressPercentage = (double)processed / TotalRecords * 100;
            }
        }
    }
}
