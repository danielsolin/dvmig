using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using dvmig.App.ViewModels;

namespace dvmig.App.Views
{
    public partial class MigrationDashboardView : UserControl
    {
        public MigrationDashboardView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(
            object sender,
            DependencyPropertyChangedEventArgs e
        )
        {
            if (e.OldValue is MigrationDashboardViewModel oldVm)
            {
                oldVm.Logs.CollectionChanged -= OnLogsCollectionChanged;
            }

            if (e.NewValue is MigrationDashboardViewModel newVm)
            {
                newVm.Logs.CollectionChanged += OnLogsCollectionChanged;
            }
        }

        private void OnLogsCollectionChanged(
            object? sender,
            NotifyCollectionChangedEventArgs e
        )
        {
            if (e.Action == NotifyCollectionChangedAction.Add)
            {
                // Wrap in Dispatcher.BeginInvoke to ensure the UI has finished
                // updating the ItemsControl before we attempt to scroll.
                // This prevents 'ItemsControl is inconsistent' exceptions.
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    if (LogList.Items.Count > 0)
                    {
                        LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
                    }
                }));
            }
        }
    }
}
