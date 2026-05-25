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
        private CancellationTokenSource? _userMappingCts;
        private bool? _targetComponentsReady;
        private bool _isCheckingTargetComponents;
        private bool _isUpdatingTargetComponents;
        private bool _isMappingUsers;
        private bool _isLoadingEntities;
        private bool _isClearingUserMappingSelection;
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
           ResetUserMappingsPanel();
           UpdateSyncButtonState();

            XTBLogWriter.AppendWelcomeBanner(
               _rtbLogs,
               "Welcome to dvmig for XrmToolBox!",
               "Please connect both a SOURCE and a TARGET environment to begin."
            );
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

               DisposeProvider(_targetProvider);
               _targetProvider = CreateProvider(newService, detail);
               _targetComponentsReady = null;
               _isCheckingTargetComponents = false;
               _isUpdatingTargetComponents = false;

               _btnSelectTarget.Text =
                  $"Target: {detail.ConnectionName}";
               _btnSelectTarget.ForeColor = Color.DarkGreen;
               _userService = null;
               ResetUserMappingsPanel();

               _rtbLogs.AppendText(
                  $"Environment '{detail.ConnectionName}' " +
                  "assigned as TARGET.\n"
               );

               CheckTargetComponents();
               StartUserMappingIfReady();
            }
            else
            {
               if (IsSameConnection(detail.ConnectionName, _targetProvider))
                  ClearTargetConnection(
                     "Target reset because it matched the selected Source.\n"
                  );

               DisposeProvider(_sourceProvider);
               _sourceProvider = CreateProvider(newService, detail);

               _btnSelectSource.Text =
                  $"Source: {detail.ConnectionName}";
               _btnSelectSource.ForeColor = Color.DarkGreen;
               _userService = null;
               ResetUserMappingsPanel();

               _rtbLogs.AppendText(
                  $"Environment '{detail.ConnectionName}' " +
                  "assigned as SOURCE.\n"
               );

               LoadEntities();
               StartUserMappingIfReady();
            }

            UpdateSyncButtonState();
        }

        private IDataverseProvider CreateProvider(
           IOrganizationService service,
           ConnectionDetail detail
        )
        {
           try
           {
              var client = detail.GetCrmServiceClient();

              if (client != null && client.IsReady)
              {
                 _rtbLogs.AppendText(
                    $"Using XrmTooling provider for " +
                    $"'{detail.ConnectionName}'.\n"
                 );

                 return new XTBToolingDataProvider(
                    client,
                    detail.ConnectionName,
                    !detail.UseOnline
                 );
              }
           }
           catch (Exception ex)
           {
              _rtbLogs.AppendText(
                 "XrmTooling provider unavailable for " +
                 $"'{detail.ConnectionName}'; using XrmToolBox service. " +
                 $"Reason: {ex.Message}\n"
              );
           }

           return new XTBDataProvider(
              service,
              detail.ConnectionName,
              !detail.UseOnline
           );
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
           DisposeProvider(_sourceProvider);
           _sourceProvider = null;
           _userService = null;
           _userMappingCts?.Cancel();
           _countCts?.Cancel();
           _allEntities.Clear();
           _selectedEntities.Clear();
           _totalRecordsCount = 0;
           _btnSelectSource.Text = "Source: Not Connected";
           _btnSelectSource.ForeColor = Color.Red;
           _clbEntities.Items.Clear();
           UpdateSelectedEntitiesLabel();
           ResetUserMappingsPanel();
           _rtbLogs.AppendText(logMessage);
        }

        private void ClearTargetConnection(string logMessage)
        {
           DisposeProvider(_targetProvider);
           _targetProvider = null;
           _userService = null;
           _userMappingCts?.Cancel();
           _targetComponentsReady = null;
           _isCheckingTargetComponents = false;
           _isUpdatingTargetComponents = false;
           _btnSelectTarget.Text = "Target: Not Connected";
           _btnSelectTarget.ForeColor = Color.Red;
           UpdateInstallComponentsButtonState();
           ResetUserMappingsPanel();
           _rtbLogs.AppendText(logMessage);
        }

        private static void DisposeProvider(IDataverseProvider? provider)
        {
           if (provider is IDisposable disposable)
              disposable.Dispose();
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
