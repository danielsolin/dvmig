using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Drawing;
using System.Threading;

using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;

using XrmToolBox.Extensibility;
using McTools.Xrm.Connection;

using dvmig.Core.Interfaces;
using dvmig.XTB.Shared;

namespace dvmig.XTB.UI
{
    public partial class MainControl : MultipleConnectionsPluginControlBase
    {
        private const string SelectSourceAction = "SelectSource";
        private const string SelectTargetAction = "SelectTarget";

        private IServiceProvider? _serviceProvider;
        private IDataverseProvider? _sourceProvider;
        private IDataverseProvider? _targetProvider;
        private IUserService? _userService;
        private List<EntityMetadata> _allEntities = new();
        private HashSet<string> _selectedEntities = new();
        private long _totalRecordsCount;
        private CancellationTokenSource? _countCts;
        private CancellationTokenSource? _syncCts;
        private bool _isFiltering;

        public MainControl()
        {
           // Optimizations for Dataverse communication in .NET Framework.
           System.Net.ServicePointManager.DefaultConnectionLimit = 65000;
           System.Net.ServicePointManager.Expect100Continue = false;
           System.Net.ServicePointManager.UseNagleAlgorithm = false;
           System.Net.ServicePointManager.SecurityProtocol |= 
              System.Net.SecurityProtocolType.Tls12;

           InitializeUI(); // UI elements must be initialized first
           _serviceProvider = 
              DIConfigurator.CreateServiceProvider(this, _rtbLogs);
           LoadPersistedSyncSettings();

            _rtbLogs.AppendText("Welcome to dvmig for XrmToolBox.\n");
            _rtbLogs.AppendText("Please connect both a SOURCE and a TARGET " +
                                "environment to begin.\n");
        }

        public override void UpdateConnection(
            IOrganizationService newService,
            ConnectionDetail detail,
            string actionName,
            object parameter
        )
        {
            if (actionName != SelectSourceAction &&
                actionName != SelectTargetAction)
            {
               _rtbLogs.AppendText(
                  string.IsNullOrEmpty(actionName)
                     ? "Connection update ignored. Use Source or Target " +
                       "to assign a connection.\n"
                     : $"Unknown connection action '{actionName}' ignored.\n"
               );

               UpdateSyncButtonState();

               return;
            }

            ResetSyncProgress();

            if (actionName == SelectTargetAction)
            {
               if (IsSameConnection(detail.ConnectionName, _sourceProvider))
                  ClearSourceConnection(
                     "Source reset because it matched the selected Target.\n"
                  );

               _targetProvider = new XTBDataProvider(
                  newService,
                  detail.ConnectionName,
                  !detail.UseOnline
               );

               _btnSelectTarget.Text =
                  $"Target: {detail.ConnectionName}";
               _btnSelectTarget.ForeColor = Color.DarkGreen;
               _userService = null;

               _rtbLogs.AppendText(
                  $"Environment '{detail.ConnectionName}' " +
                  "assigned as TARGET.\n"
               );
            }
            else
            {
               if (IsSameConnection(detail.ConnectionName, _targetProvider))
                  ClearTargetConnection(
                     "Target reset because it matched the selected Source.\n"
                  );

               _sourceProvider = new XTBDataProvider(
                  newService,
                  detail.ConnectionName,
                  !detail.UseOnline
               );

               _btnSelectSource.Text =
                  $"Source: {detail.ConnectionName}";
               _btnSelectSource.ForeColor = Color.DarkGreen;
               _userService = null;

               _rtbLogs.AppendText(
                  $"Environment '{detail.ConnectionName}' " +
                  "assigned as SOURCE.\n"
               );

               LoadEntities();
            }

            UpdateSyncButtonState();
        }

        private static bool IsSameConnection(
           string connectionName,
           IDataverseProvider? provider
        )
        {
           return provider != null &&
              string.Equals(
                 connectionName.Trim(),
                 provider.ConnectionString.Trim(),
                 StringComparison.OrdinalIgnoreCase
              );
        }

        private void ClearSourceConnection(string logMessage)
        {
           _sourceProvider = null;
           _userService = null;
           _countCts?.Cancel();
           _allEntities.Clear();
           _selectedEntities.Clear();
           _totalRecordsCount = 0;
           _btnSelectSource.Text = "Source: Not Connected";
           _btnSelectSource.ForeColor = Color.Red;
           _clbEntities.Items.Clear();
           UpdateSelectedEntitiesLabel();
           _rtbLogs.AppendText(logMessage);
        }

        private void ClearTargetConnection(string logMessage)
        {
           _targetProvider = null;
           _userService = null;
           _btnSelectTarget.Text = "Target: Not Connected";
           _btnSelectTarget.ForeColor = Color.Red;
           _rtbLogs.AppendText(logMessage);
        }

        private void ResetSyncProgress()
        {
           _prgSync.Value = 0;
        }

        protected override void ConnectionDetailsUpdated(
            NotifyCollectionChangedEventArgs e
        )
        {
            // Here because an implementation is required.
        }

        protected override void OnHandleCreated(EventArgs e)
        {
           base.OnHandleCreated(e);
           ApplyPreferredSplitterLayout();
        }

        private class EntityItem
        {
            public EntityMetadata Metadata { get; }
            public EntityItem(EntityMetadata metadata) => Metadata = metadata;
            public override string ToString() =>
                $"{Metadata.DisplayName?.UserLocalizedLabel?.Label
                   ?? Metadata.LogicalName} ({Metadata.LogicalName})";
        }
    }
}
