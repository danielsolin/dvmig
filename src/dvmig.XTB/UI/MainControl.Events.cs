using System;
using System.Collections.Generic;
using System.Windows.Forms;

using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Extensions.DependencyInjection;

using XrmToolBox.Extensibility;

using dvmig.Core.Interfaces;
using dvmig.Core.Synchronization;
using dvmig.XTB.Shared;

namespace dvmig.XTB.UI
{
   public partial class MainControl
   {
      private const string _txtSearchEntities = "Search entities...";

      private void OnSelectTargetClick(object? sender, EventArgs e)
      {
         AddAdditionalOrganization();
      }

      private void OnSearchGotFocus(object? sender, EventArgs e)
      {
         if(_txtSearch.Text == _txtSearchEntities)
         {
            _txtSearch.Text = "";
            _txtSearch.ForeColor = System.Drawing.Color.Black;
         }
      }

      private void OnSearchLostFocus(object? sender, EventArgs e)
      {
         if(string.IsNullOrWhiteSpace(_txtSearch.Text))
         {
            _txtSearch.Text = _txtSearchEntities;
            _txtSearch.ForeColor = System.Drawing.Color.Gray;
         }
      }

      private void OnSearchTextChanged(object? sender, EventArgs e)
      {
         FilterEntities();
      }

      private void UpdateSyncButtonState()
      {
         _btnSync.Enabled = _sourceProvider != null &&
            _targetProvider != null;
      }

      private void LoadEntities()
      {
         if(_sourceProvider == null || _serviceProvider == null)
            return;

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
               if(args.Error != null)
               {
                  _rtbLogs.AppendText(
                         $"Error fetching entities: {args.Error.Message}\n"
                      );

                  return;
               }

               if(args.Result is List<EntityMetadata> entities)
               {
                  _allEntities = entities;
                  FilterEntities();
               }
            }
         });
      }

      private void FilterEntities()
      {
         var filter = (_txtSearch.Text == _txtSearchEntities)
            ? string.Empty
            : _txtSearch.Text.ToLowerInvariant();

         _clbEntities.BeginUpdate();
         _clbEntities.Items.Clear();

         foreach(var entity in _allEntities)
         {
            var displayName = entity
               .DisplayName?
               .UserLocalizedLabel?
               .Label ?? entity.LogicalName;

            if(string.IsNullOrEmpty(filter) ||
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
         if(_clbEntities.CheckedItems.Count == 0)
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
         foreach(EntityItem item in _clbEntities.CheckedItems)
            selectedLogicalNames.Add(item.Metadata.LogicalName);

         if(_sourceProvider == null || _targetProvider == null
            || _serviceProvider == null)
            return;

         _btnSync.Enabled = false;
         _clbEntities.Enabled = false;
         _rtbLogs.Clear();
         _prgSync.Visible = true;
         _prgSync.Value = 0;

         WorkAsync(new WorkAsyncInfo
         {
            Message = "Preparing synchronization...",
            Work = (worker, args) =>
            {
               if(_userService == null)
               {
                  _userService = new UserService(
                     _serviceProvider!.GetRequiredService<ILogger>(),
                     _sourceProvider,
                     _targetProvider
                  );
               }

               var job = new SynchronizationJob(
                  _serviceProvider!,
                  _sourceProvider!,
                  _targetProvider!,
                  _userService,
                  selectedLogicalNames,
                  (percent, message) => worker.ReportProgress(percent, message)
               );
               job.Run();
            },
            ProgressChanged = e =>
            {
               _prgSync.Value = e.ProgressPercentage;
               SetWorkingMessage(
                  e.UserState?.ToString() ?? "Synchronizing..."
               );
            },
            PostWorkCallBack = (args) =>
            {
               _prgSync.Visible = false;
               _clbEntities.Enabled = true;
               UpdateSyncButtonState();

               if(args.Error != null)
               {
                  _rtbLogs.AppendText(
                     $"\n[ERROR] Sync failed: {args.Error.Message}\n"
                  );

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
   }
}
