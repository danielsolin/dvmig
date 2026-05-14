using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Drawing;

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
        private IServiceProvider? _serviceProvider;
        private IDataverseProvider? _sourceProvider;
        private IDataverseProvider? _targetProvider;
        private IUserService? _userService;
        private List<EntityMetadata> _allEntities = new();
        private HashSet<string> _selectedEntities = new();
        private long _totalRecordsCount;

        public MainControl()
        {
            InitializeUI(); // UI elements must be initialized first
            _serviceProvider = DIConfigurator.CreateServiceProvider(this, _rtbLogs);

            _rtbLogs.AppendText("Welcome to dvmig for XrmToolBox.\n");
            _rtbLogs.AppendText("Please connect both a SOURCE and a TARGET " +
                                "environment to begin.\n");
            _rtbLogs.AppendText("Note: Any environment connected during " +
                                "startup is assigned as the SOURCE.\n\n");
        }

        public override void UpdateConnection(
            IOrganizationService newService,
            ConnectionDetail detail,
            string actionName,
            object parameter
        )
        {
            if (string.IsNullOrEmpty(actionName))
            {
               base.UpdateConnection(newService, detail, actionName, parameter);
            }

            ResetSyncProgress();

            if (actionName == "SelectTarget")
            {
               _targetProvider = new XTBDataProvider(
                  newService,
                  detail.ConnectionName
               );

               _lblTarget.Text = $"Target: {detail.OrganizationFriendlyName}";
               _lblTarget.ForeColor = Color.DarkGreen;
               _userService = null;

               _rtbLogs.AppendText($"Environment '{detail.OrganizationFriendlyName}' " +
                                   "assigned as TARGET.\n");
            }
            else
            {
               _sourceProvider = new XTBDataProvider(
                  newService,
                  detail.ConnectionName
               );

               _lblSource.Text = $"Source: {detail.OrganizationFriendlyName}";
               _lblSource.ForeColor = Color.DarkGreen;
               _userService = null;

               _rtbLogs.AppendText($"Environment '{detail.OrganizationFriendlyName}' " +
                                   "assigned as SOURCE.\n");

               LoadEntities();
            }

            UpdateSyncButtonState();
        }

        private void ResetSyncProgress()
        {
           _prgSync.Value = 0;
           _prgSync.Visible = false;
        }

        protected override void ConnectionDetailsUpdated(
            NotifyCollectionChangedEventArgs e
        )
        {
            // Here because an implementation is required.
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
