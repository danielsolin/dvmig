using System;
using System.Collections.Generic;
using System.Collections.Specialized;

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

        public MainControl()
        {
            InitializeUI(); // UI elements must be initialized first
            _serviceProvider = DIConfigurator.CreateServiceProvider(this, _rtbLogs);
        }

        public override void UpdateConnection(
            IOrganizationService newService,
            ConnectionDetail detail,
            string actionName,
            object parameter
        )
        {
            base.UpdateConnection(newService, detail, actionName, parameter);

            if (_sourceProvider == null)
            {
               _sourceProvider = new XTBDataProvider(
                  newService,
                  detail.ConnectionName
               );

               _userService = null;
               LoadEntities();
            }
            else if (_sourceProvider.ConnectionString != detail.ConnectionName)
            {
               _targetProvider = new XTBDataProvider(
                  newService,
                  detail.ConnectionName
               );

               _userService = null;
            }

            UpdateSyncButtonState();
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
