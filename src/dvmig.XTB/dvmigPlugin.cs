using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Collections.Specialized;
using System.Windows.Forms;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.IO;

using Microsoft.Xrm.Sdk;
using Microsoft.Extensions.DependencyInjection;
using McTools.Xrm.Connection;

using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Interfaces;

using dvmig.Core.Interfaces;
using dvmig.Core.Provisioning;
using dvmig.Core.Synchronization;
using dvmig.XTB.Settings;
using dvmig.XTB.Shared;

using Label = System.Windows.Forms.Label;
using Microsoft.Xrm.Sdk.Metadata;

namespace dvmig.XTB
{
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

   public class MainControl : MultipleConnectionsPluginControlBase
   {
      private IServiceProvider? _serviceProvider;
      private IDataverseProvider? _sourceProvider;
      private IDataverseProvider? _targetProvider;
      private List<Microsoft.Xrm.Sdk.Metadata.EntityMetadata> _allEntities = 
         new List<Microsoft.Xrm.Sdk.Metadata.EntityMetadata>();

      // UI Components
      private Label _lblTarget;
      private Label _lblSource;
      private Button _btnSelectSource;
      private Button _btnSync;
      private CheckedListBox _clbEntities;
      private TextBox _txtSearch;
      private RichTextBox _rtbLogs;
      private SplitContainer _mainSplit;

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
         services.AddTransient<IValidationService, ValidationService>();
         services.AddTransient<ISchemaService, SchemaService>();
         services.AddTransient<IPluginService, PluginService>();

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
         // Top Panel (Connection Info)
         var topPanel = new TableLayoutPanel
         {
            Dock = DockStyle.Top,
            Height = 85,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(10)
         };
         topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
         topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));

         _lblTarget = new Label
         {
            Text = "Target: Not Connected",
            AutoSize = true,
            Font = new Font(FontFamily.GenericSansSerif, 9, FontStyle.Bold)
         };

         _lblSource = new Label
         {
            Text = "Source: Not Connected",
            AutoSize = true,
            ForeColor = Color.DarkRed,
            Margin = new Padding(0, 5, 0, 0)
         };

         _btnSelectSource = new Button
         {
            Text = "Change Source",
            Dock = DockStyle.Fill
         };
         _btnSelectSource.Click += (s, e) => AddAdditionalOrganization();

         _btnSync = new Button
         {
            Text = "Run Synchronization",
            Dock = DockStyle.Fill,
            Enabled = false,
            BackColor = Color.LightGreen
         };
         _btnSync.Click += RunSync_Click;

         topPanel.Controls.Add(_lblTarget, 0, 0);
         topPanel.Controls.Add(_btnSelectSource, 1, 0);
         topPanel.Controls.Add(_lblSource, 0, 1);
         topPanel.Controls.Add(_btnSync, 1, 1);

         // Main Content (Split)
         _mainSplit = new SplitContainer
         {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 350
         };

         // Left Panel (Entities)
         var leftPanel = new Panel { Dock = DockStyle.Fill };
         
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

         leftPanel.Controls.Add(_clbEntities);
         leftPanel.Controls.Add(_txtSearch);

         // Right Panel (Logs)
         _rtbLogs = new RichTextBox
         {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BackColor = Color.Black,
            ForeColor = Color.LightGray,
            Font = new Font("Consolas", 9)
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

         // XrmToolBox calls UpdateConnection for every connection update.
         // We only want to set the Target from the main connection selection.
         if (actionName == "AdditionalOrganization")
            return;

         _targetProvider = new Providers.XrmToolBoxDataProvider(
            newService,
            detail.ConnectionName
         );

         _lblTarget.Text = $"Target: {detail.ConnectionName}";
         _lblTarget.ForeColor = Color.DarkGreen;
         
         UpdateSyncButtonState();
      }

      protected override void ConnectionDetailsUpdated(
         NotifyCollectionChangedEventArgs e
      )
      {
         if (e.Action == NotifyCollectionChangedAction.Add
               && e.NewItems != null)
         {
            foreach (ConnectionDetail detail in e.NewItems)
            {
               var service = detail.GetCrmServiceClient();
               _sourceProvider = new Providers.XrmToolBoxDataProvider(
                  service,
                  detail.ConnectionName
               );

               _lblSource.Text = $"Source: {detail.ConnectionName}";
               _lblSource.ForeColor = Color.DarkBlue;
               
               LoadEntities();
               UpdateSyncButtonState();
               break;
            }
         }
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

      private void UpdateSyncButtonState()
      {
         _btnSync.Enabled = _sourceProvider != null && _targetProvider != null;
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

         if (_sourceProvider == null || _targetProvider == null || _serviceProvider == null)
            return;

         _btnSync.Enabled = false;
         _clbEntities.Enabled = false;
         _rtbLogs.Clear();

         WorkAsync(new WorkAsyncInfo
         {
            Message = "Running synchronization...",
            Work = (worker, args) =>
            {
               var logger = _serviceProvider.GetRequiredService<ILogger>();
               var validator = _serviceProvider.GetRequiredService<IValidationService>();
               var schemaService = _serviceProvider.GetRequiredService<ISchemaService>();
               var pluginService = _serviceProvider.GetRequiredService<IPluginService>();
               var syncStateService = _serviceProvider.GetRequiredService<ISyncStateService>();

               // 1. Validate Target
               logger.Information("Validating target environment...");
               if (!validator.ValidateTargetEnvironmentAsync(_targetProvider).GetAwaiter().GetResult())
               {
                  logger.Information("Target environment not prepared. Installing components...");
                  
                  // Find plugin path relative to current assembly
                  var pluginDir = Path.GetDirectoryName(typeof(MainControl).Assembly.Location);
                  var pluginPath = Path.Combine(pluginDir ?? "", "dvmig.Plugins.dll");

                  schemaService.CreateSchemaAsync(_targetProvider).GetAwaiter().GetResult();
                  pluginService.DeployPluginAsync(_targetProvider, pluginPath).GetAwaiter().GetResult();
               }

               // 2. Initialize bound services
               var userService = new UserService(logger, _sourceProvider, _targetProvider);
               var entityService = new EntityService(logger, _targetProvider);

               var syncEngine = new SyncEngine(
                  _sourceProvider,
                  _targetProvider,
                  userService,
                  logger,
                  entityService,
                  syncStateService
               );
               
               var options = new SyncOptions
               {
                  MaxDegreeOfParallelism = 4,
                  ForceResync = false,
                  PreserveAuditData = true,
                  StripMissingDependencies = true
               };

               // Run Async and wait for it
               foreach (var entityLogicalName in selectedLogicalNames)
               {
                  logger.Information($"Starting sync for {entityLogicalName}...");
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
                  _rtbLogs.AppendText($"\n[ERROR] Synchronization failed: {args.Error.Message}\n");
                  MessageBox.Show(
                     $"Sync failed: {args.Error.Message}",
                     "Error",
                     MessageBoxButtons.OK,
                     MessageBoxIcon.Error
                  );
               }
               else
               {
                  _rtbLogs.AppendText("\n[SUCCESS] Synchronization complete!\n");
                  MessageBox.Show("Synchronization completed successfully.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
               }
               
               _rtbLogs.ScrollToCaret();
            }
         });
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
