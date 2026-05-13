using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Collections.Specialized;
using System.Windows.Forms;
using System.Drawing;

using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Extensions.DependencyInjection;
using McTools.Xrm.Connection;

using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Interfaces;

using dvmig.Core.Interfaces;
using dvmig.Core.Provisioning;
using dvmig.Core.Synchronization;
using dvmig.XTB.Settings;
using dvmig.XTB.Shared;
using IEnvironmentService = dvmig.Core.Interfaces.IEnvironmentService;

namespace dvmig.XTB
{
   #region DvmigPlugin
   [Export(typeof(IXrmToolBoxPlugin))]
   [ExportMetadata("Name", "Dataverse Migration (dvmig)")]
   [ExportMetadata("SmallImageBase64", null)]
   [ExportMetadata("BigImageBase64", null)]
   [ExportMetadata("BackgroundColor", "White")]
   [ExportMetadata("PrimaryFontColor", "Black")]
   [ExportMetadata("SecondaryFontColor", "Gray")]
   [ExportMetadata(
      "Description",
      "Synchronize data between environments with audit preservation."
   )]
   public class DvmigPlugin : PluginBase
   {
      public override IXrmToolBoxPluginControl GetControl()
      {
         return new MainControl();
      }
   }
   #endregion

   public class MainControl : MultipleConnectionsPluginControlBase
   {
      private IServiceProvider? _serviceProvider;
      private IDataverseProvider? _sourceProvider;
      private IDataverseProvider? _targetProvider;
      private List<EntityMetadata> _allEntities = new();

      private Button _btnSelectTarget = null!;
      private Button _btnSync = null!;
      private CheckedListBox _clbEntities = null!;
      private TextBox _txtSearch = null!;
      private RichTextBox _rtbLogs = null!;
      private SplitContainer _mainSplit = null!;

      public MainControl()
      {
         InitializeServices();
         InitializeUI();
      }

      private void InitializeServices()
      {
         var services = new ServiceCollection();

         // Shared Infrastructure
         var logger = new XrmToolBoxLogger(this);
         services.AddSingleton<ILogger>(logger);
         services.AddSingleton<ISettingsService>(
            new XrmToolBoxSettingsService(this)
         );
         services.AddSingleton<ISyncStateService, SyncStateService>();

         // Metadata & Synchronization
         services.AddTransient<IEntityService, EntityService>();
         services.AddTransient<IUserService, UserService>();
         services.AddTransient<ISeedingService, SeedingService>();
         services.AddTransient<IWipeDataService, WipeDataService>();
         services.AddTransient<IEnvironmentService, EnvironmentService>();

         _serviceProvider = services.BuildServiceProvider();

         // Attach UI log update
         var progress = new Progress<string>(msg =>
         {
            if (_rtbLogs.InvokeRequired)
            {
               _rtbLogs.Invoke(new Action(() => 
               {
                  _rtbLogs.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
                  _rtbLogs.ScrollToCaret();
               }));
            }
            else
            {
               _rtbLogs.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
               _rtbLogs.ScrollToCaret();
            }
         });
         logger.AttachProgress(progress);
      }

      private void InitializeUI()
      {
         var topPanel = new TableLayoutPanel
         {
            Dock = DockStyle.Top,
            Height = 85,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(10)
         };
         topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 80));
         topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));

         _btnSelectTarget = new Button
         {
            Text = "Select Target",
            Dock = DockStyle.Fill
         };
         _btnSelectTarget.Click += (s, e) => AddAdditionalOrganization();

         _btnSync = new Button
         {
            Text = "Run Synchronization",
            Dock = DockStyle.Fill,
            Enabled = false,
            BackColor = Color.LightGreen
         };
         _btnSync.Click += RunSync_Click;

         topPanel.Controls.Add(_btnSelectTarget, 1, 0);
         topPanel.Controls.Add(_btnSync, 1, 1);

         _txtSearch = new TextBox
         {
            Dock = DockStyle.Top,
         };

         _txtSearch.Text = "Search entities...";
         _txtSearch.ForeColor = Color.Gray;
         _txtSearch.GotFocus += (s, e) => 
         {
            if (_txtSearch.Text == "Search entities...")
            {
               _txtSearch.Text = "";
               _txtSearch.ForeColor = Color.Black;
            }
         };
         _txtSearch.LostFocus += (s, e) =>
         {
            if (string.IsNullOrWhiteSpace(_txtSearch.Text))
            {
               _txtSearch.Text = "Search entities...";
               _txtSearch.ForeColor = Color.Gray;
            }
         };
         _txtSearch.TextChanged += (s, e) => FilterEntities();

         _clbEntities = new CheckedListBox
         {
            Dock = DockStyle.Fill,
            CheckOnClick = true,
            IntegralHeight = false
         };

         _rtbLogs = new RichTextBox
         {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BackColor = Color.Black,
            ForeColor = Color.LightGray,
            Font = new Font("Consolas", 9)
         };

         var leftPanel = new Panel { Dock = DockStyle.Fill };
         leftPanel.Controls.Add(_clbEntities);
         leftPanel.Controls.Add(_txtSearch);

         _mainSplit = new SplitContainer
         {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical
         };
         _mainSplit.Panel1.Controls.Add(leftPanel);
         _mainSplit.Panel2.Controls.Add(_rtbLogs);

         Controls.Add(_mainSplit);
         Controls.Add(topPanel);
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
            _sourceProvider = new Providers.XrmToolBoxDataProvider(
               newService, 
               detail.ConnectionName
            );
            LoadEntities();
         }
         else if (_sourceProvider.ConnectionString != detail.ConnectionName)
         {
            _targetProvider = new Providers.XrmToolBoxDataProvider(
               newService, 
               detail.ConnectionName
            );
         }
         
         UpdateSyncButtonState();
      }

      private void LoadEntities()
      {
         if (_sourceProvider == null || _serviceProvider == null) return;

         _clbEntities.Items.Clear();
         _clbEntities.Items.Add("Loading entities...");
         _clbEntities.Enabled = false;

         WorkAsync(new WorkAsyncInfo
         {
            Message = "Fetching entities from source...",
            Work = (worker, args) =>
            {
               var entityService =
                  _serviceProvider.GetRequiredService<IEntityService>();

               args.Result = entityService
                              .GetMigrationEntitiesAsync(_sourceProvider)
                              .GetAwaiter()
                              .GetResult();
            },
            PostWorkCallBack = (args) =>
            {
               _clbEntities.Enabled = true;
               if (args.Error != null)
               {
                  _rtbLogs.AppendText(
                     $"Error fetching entities: {args.Error.Message}\n"
                  );

                  return;
               }

               if (args.Result is List<EntityMetadata> entities)
               {
                  _allEntities = entities;
                  FilterEntities();
               }
            }
         });
      }
      
      private void FilterEntities()
      {
         var filter = (_txtSearch.Text == "Search entities...") 
            ? string.Empty 
            : _txtSearch.Text.ToLowerInvariant();

         _clbEntities.BeginUpdate();
         _clbEntities.Items.Clear();

         foreach (var entity in _allEntities)
         {
            var displayName = entity
                                 .DisplayName?
                                 .UserLocalizedLabel?
                                 .Label ?? entity.LogicalName;

            if (string.IsNullOrEmpty(filter) || 
                displayName.ToLowerInvariant().Contains(filter) || 
                entity.LogicalName.ToLowerInvariant().Contains(filter))
            {
               _clbEntities.Items.Add(new EntityItem(entity));
            }
         }
         _clbEntities.EndUpdate();
      }

      private void RunSync_Click(object? sender, EventArgs e)
      {
         if (_clbEntities.CheckedItems.Count == 0)
         {
            MessageBox.Show(
               "Please select at least one entity to synchronize.",
               "No Entities Selected",
               MessageBoxButtons.OK,
               MessageBoxIcon.Warning
            );

            return;
         }

         var selectedLogicalNames = new List<string>();
         foreach (EntityItem item in _clbEntities.CheckedItems)
            selectedLogicalNames.Add(item.Metadata.LogicalName);

         if (_sourceProvider == null || _targetProvider == null
               || _serviceProvider == null)
            return;

         _btnSync.Enabled = false;
         _clbEntities.Enabled = false;
         _rtbLogs.Clear();

         WorkAsync(new WorkAsyncInfo
         {
            Message = "Preparing synchronization...",
            Work = (worker, args) =>
            {
               var logger =
                  _serviceProvider.GetRequiredService<ILogger>();
               var envService =
                  _serviceProvider.GetRequiredService<IEnvironmentService>();
               var syncStateService =
                  _serviceProvider.GetRequiredService<ISyncStateService>();

               var userService =
                  new UserService(logger, _sourceProvider, _targetProvider);
               var entityService =
                  new EntityService(logger, _targetProvider);

               // 1. Ensure target is ready
               logger.Information("Validating target environment...");
               var isReady = envService
                  .ValidateTargetEnvironmentAsync(_targetProvider)
                  .GetAwaiter()
                  .GetResult();

               if (!isReady)
               {
                  logger.Warning(
                     "Target environment is not initialized. " +
                     "Installing components..."
                  );

                  envService
                     .InstallComponentsAsync(_targetProvider)
                     .GetAwaiter()
                     .GetResult();

                  logger.Information("Target environment initialized.");
               }

               var syncEngine = new SyncEngine(
                  _sourceProvider,
                  _targetProvider,
                  userService,
                  logger,
                  entityService,
                  syncStateService
               );

               // 2. Initialize Engine (Maps users etc)
               logger.Information("Initializing synchronization engine...");
               syncEngine.InitializeSyncAsync().GetAwaiter().GetResult();
               
               var options = new SyncOptions
               {
                  MaxDegreeOfParallelism = 4,
                  ForceResync = false,
                  PreserveAuditData = true,
                  StripMissingDependencies = true
               };

               // 3. Run Sync for selected entities
               foreach (var entityLogicalName in selectedLogicalNames)
               {
                  logger.Information(
                     $"Starting sync for {entityLogicalName}..."
                  );

                  syncEngine.SyncAsync(
                     entityLogicalName, 
                     options
                  ).GetAwaiter().GetResult();
               }
            },
            PostWorkCallBack = (args) =>
            {
               _clbEntities.Enabled = true;
               UpdateSyncButtonState();

               if (args.Error != null)
               {
                  _rtbLogs.AppendText(
                     $"\n[ERROR] Synchronization failed: " +
                     $"{args.Error.Message}\n");

                  MessageBox.Show(
                     $"Sync failed: {args.Error.Message}",
                     "Error",
                     MessageBoxButtons.OK,
                     MessageBoxIcon.Error
                  );
               }
               else
               {
                  _rtbLogs.AppendText(
                     "\n[SUCCESS] Synchronization complete!\n"
                  );

                  MessageBox.Show(
                     "Synchronization completed successfully.",
                     "Success",
                     MessageBoxButtons.OK,
                     MessageBoxIcon.Information
                  );
               }
               
               _rtbLogs.ScrollToCaret();
            }
         });
      }

      private void UpdateSyncButtonState()
      {
         _btnSync.Enabled = _sourceProvider != null && _targetProvider != null;
      }

      protected override void ConnectionDetailsUpdated(NotifyCollectionChangedEventArgs e)
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
