using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Extensions.DependencyInjection;

using XrmToolBox.Extensibility;

using dvmig.Core.Interfaces;
using dvmig.Core.Providers;
using dvmig.Core.Shared;
using dvmig.Core.Synchronization;
using dvmig.XTB.Shared;

namespace dvmig.XTB.UI
{
   public partial class MainControl
   {
      private const string _searchPlaceholderText = "Search entities...";

      private void OnSelectSourceClick(object? sender, EventArgs e)
      {
         RaiseRequestConnectionEvent(new RequestConnectionEventArgs
         {
            ActionName = "SelectSource",
            Control = this
         });
      }

      private void OnSelectTargetClick(object? sender, EventArgs e)
      {
         RaiseRequestConnectionEvent(new RequestConnectionEventArgs
         {
            ActionName = "SelectTarget",
            Control = this
         });
      }

      private void OnSearchGotFocus(object? sender, EventArgs e)
      {
         if(_txtSearch.Text == _searchPlaceholderText)
         {
            _txtSearch.Text = "";
            _txtSearch.ForeColor = System.Drawing.Color.Black;
         }
      }

      private void OnSearchLostFocus(object? sender, EventArgs e)
      {
         if(string.IsNullOrWhiteSpace(_txtSearch.Text))
         {
            _txtSearch.Text = _searchPlaceholderText;
            _txtSearch.ForeColor = System.Drawing.Color.Gray;
         }
      }

      private void OnSearchTextChanged(object? sender, EventArgs e)
      {
         FilterEntities();
      }

      private void OnEntityItemCheck(object? sender, ItemCheckEventArgs e)
      {
         if (_isFiltering) return;

         if (_clbEntities.Items[e.Index] is EntityItem item)
         {
            if (e.NewValue == CheckState.Checked)
               _selectedEntities.Add(item.Metadata.LogicalName);
            else
               _selectedEntities.Remove(item.Metadata.LogicalName);

            UpdateSelectedEntitiesLabel();
            ResetSyncProgress();
         }
      }

      private void OnSyncOptionsChanged(object? sender, EventArgs e)
      {
         ResetSyncProgress();
      }

      private void SelectRecommended_Click(object? sender, EventArgs e)
      {
         if (_allEntities.Count == 0)
         {
            MessageBox.Show(
               "Connect a source environment before selecting recommended entities.",
               "No Source Connected",
               MessageBoxButtons.OK,
               MessageBoxIcon.Information
            );

            return;
         }

         var availableEntities = _allEntities
            .Select(e => e.LogicalName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

         _selectedEntities = SystemConstants.SyncSettings
            .RecommendedEntities
            .Where(availableEntities.Contains)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

         FilterEntities();
         UpdateSelectedEntitiesLabel();
         ResetSyncProgress();
      }

      private void UpdateSelectedEntitiesLabel()
      {
         if (_selectedEntities.Count == 0)
         {
            _lblSelectedEntities.Text = "Selected: None";
            _totalRecordsCount = 0;
            return;
         }

         var names = string.Join(", ", _selectedEntities);
         _lblSelectedEntities.Text = $"Selected ({_selectedEntities.Count}):" +
            $" {names} | Records: Counting...";
         
         CalculateTotalRecords();
      }

      private void CalculateTotalRecords()
      {
         if (_sourceProvider == null) return;

         _countCts?.Cancel();
         _countCts = new CancellationTokenSource();
         var token = _countCts.Token;

         var selectedCopy = new List<string>(_selectedEntities);

         Task.Run(async () =>
         {
            try
            {
               long total = 0;
               foreach (var name in selectedCopy)
               {
                  if (token.IsCancellationRequested) return;

                  total += await _sourceProvider.GetRecordCountAsync(name, ct: token);
               }

               if (token.IsCancellationRequested) return;

               _lblSelectedEntities.Invoke(new Action(() =>
               {
                  _totalRecordsCount = total;
                  var names = string.Join(", ", _selectedEntities);
                  _lblSelectedEntities.Text = $"Selected " +
                     $"({_selectedEntities.Count}): {names} " +
                     $"| Records: {total:N0}";
               }));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
               _rtbLogs.Invoke(new Action(() =>
               {
                  _rtbLogs.AppendText($"Error counting records: {ex.Message}\n");
               }));
            }
         }, token);
      }

      private void UpdateSyncButtonState()
      {
         _btnSync.Enabled = _sourceProvider != null &&
            _targetProvider != null &&
            _syncCts == null;

         _btnCancelSync.Enabled = _syncCts != null;
         _btnSelectRecommended.Enabled = _syncCts == null;
         _chkForceResync.Enabled = _syncCts == null;
      }

      private void LoadEntities()
      {
         if(_sourceProvider == null || _serviceProvider == null)
            return;

         _countCts?.Cancel();
         _selectedEntities.Clear();
         _totalRecordsCount = 0;
         UpdateSelectedEntitiesLabel();
         ResetSyncProgress();
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
         var filter = (_txtSearch.Text == _searchPlaceholderText)
            ? string.Empty
            : _txtSearch.Text.ToLowerInvariant();

         _isFiltering = true;
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
               var isChecked = _selectedEntities.Contains(entity.LogicalName);
               _clbEntities.Items.Add(new EntityItem(entity), isChecked);
            }
         }
         _clbEntities.EndUpdate();
         _isFiltering = false;
      }

      private void RunSync_Click(object? sender, EventArgs e)
      {
         if(_selectedEntities.Count == 0)
         {
            MessageBox.Show(
               "Please select at least one entity to synchronize.",
               "No Entities Selected",
               MessageBoxButtons.OK,
               MessageBoxIcon.Warning
            );

            return;
         }

         var selectedLogicalNames = new List<string>(_selectedEntities);

         if(_sourceProvider == null || _targetProvider == null
            || _serviceProvider == null)
            return;

         _countCts?.Cancel();
         _syncCts = new CancellationTokenSource();
         _btnSync.Enabled = false;
         _btnCancelSync.Enabled = true;
         _btnSelectRecommended.Enabled = false;
         _chkForceResync.Enabled = false;
         _clbEntities.Enabled = false;
         _rtbLogs.Clear();
         _prgSync.Value = 0;

         WorkAsync(new WorkAsyncInfo
         {
            Message = "Preparing synchronization...",
            Work = (worker, args) =>
            {
               if (_userService == null)
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
                  (int)_totalRecordsCount,
                  _chkForceResync.Checked,
                  (percent, message) => worker.ReportProgress(percent, message)
               );

               try
               {
                  job.Run(_syncCts.Token);
               }
               catch (OperationCanceledException)
               {
                  args.Cancel = true;
               }
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
               _syncCts?.Dispose();
               _syncCts = null;
               _clbEntities.Enabled = true;
               UpdateSyncButtonState();

               if(args.Cancelled)
               {
                  _prgSync.Value = 0;
                  _rtbLogs.AppendText("\n[INFO] Synchronization cancelled.\n");

                  MessageBox.Show(
                     "Synchronization cancelled.",
                     "Cancelled",
                     MessageBoxButtons.OK,
                     MessageBoxIcon.Information
                  );
               }
               else if(args.Error != null)
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
                  _prgSync.Value = 100;
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

      private void CancelSync_Click(object? sender, EventArgs e)
      {
         _syncCts?.Cancel();
         _btnCancelSync.Enabled = false;
         SetWorkingMessage("Cancelling synchronization...");
      }
   }
}
