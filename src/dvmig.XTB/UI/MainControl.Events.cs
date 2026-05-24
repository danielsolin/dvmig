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
            ActionName = SelectSourceAction,
            Control = this
         });
      }

      private void OnSelectTargetClick(object? sender, EventArgs e)
      {
         RaiseRequestConnectionEvent(new RequestConnectionEventArgs
         {
            ActionName = SelectTargetAction,
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

      private void OnShowHiddenEntitiesChanged(object? sender, EventArgs e)
      {
         LoadEntities(resetSelection: false);
      }

      private void OnMainSplitSizeChanged(object? sender, EventArgs e)
      {
         ApplyPreferredSplitterLayout();
      }

      private void ApplyPreferredSplitterLayout()
      {
         if (_mainSplit == null ||
             _mainSplit.Width <= _mainSplit.Panel1MinSize)
            return;

         var minimumLeftWidth = _mainSplit.Panel1MinSize;
         var maximumLeftWidth =
            _mainSplit.Width - _mainSplit.SplitterWidth - 1;

         var desiredLeftWidth = 360;
         var splitterDistance = Math.Max(minimumLeftWidth, desiredLeftWidth);
         splitterDistance = Math.Min(splitterDistance, maximumLeftWidth);

         if (splitterDistance < minimumLeftWidth)
            return;

         if (_mainSplit.SplitterDistance == splitterDistance)
            return;

         _mainSplit.SplitterDistance = splitterDistance;
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
         _lblSyncStatus.Text = "Ready";
         ResetSyncProgress();
      }

      private void OnAutoCreateRelatedRecordsChanged(
         object? sender,
         EventArgs e
      )
      {
         SaveAutoCreateRelatedRecordsSetting();
         OnSyncOptionsChanged(sender, e);
      }

      private void LoadPersistedSyncSettings()
      {
         if (_serviceProvider == null)
            return;

         var settings = _serviceProvider
            .GetRequiredService<ISettingsService>()
            .LoadSettings();

         _chkAutoCreateRelatedRecords.Checked =
            settings.AutoCreateRelatedRecords;
      }

      private void SaveAutoCreateRelatedRecordsSetting()
      {
         if (_serviceProvider == null)
            return;

         var settingsService = _serviceProvider
            .GetRequiredService<ISettingsService>();
         var settings = settingsService.LoadSettings();
         settings.AutoCreateRelatedRecords =
            _chkAutoCreateRelatedRecords.Checked;
         settingsService.SaveSettings(settings);
      }

      private void OnSelectRecommendedChanged(object? sender, EventArgs e)
      {
         var availableEntities = _allEntities
            .Select(e => e.LogicalName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

         var recommendedEntities = SystemConstants.SyncSettings
            .RecommendedEntities
            .Where(availableEntities.Contains)
            .ToList();

         foreach (var logicalName in recommendedEntities)
         {
            if (_chkSelectRecommended.Checked)
               _selectedEntities.Add(logicalName);
            else
               _selectedEntities.Remove(logicalName);
         }

         FilterEntities();
         UpdateSelectedEntitiesLabel();
         ResetSyncProgress();
      }

      private void UpdateSelectedEntitiesLabel()
      {
         _selectedEntityChipsPanel.Controls.Clear();

         if (_selectedEntities.Count == 0)
         {
            _lblSelectedEntities.Text = "Selected: None";
            _totalRecordsCount = 0;
            return;
         }

         _lblSelectedEntities.Text =
            $"Selected ({_selectedEntities.Count}) | Records: Counting...";

         foreach (var logicalName in _selectedEntities.OrderBy(e => e))
         {
            _selectedEntityChipsPanel.Controls.Add(
               CreateSelectedEntityChip(logicalName)
            );
         }
         
         CalculateTotalRecords();
      }

      private Label CreateSelectedEntityChip(string logicalName)
      {
         return new Label
         {
            AutoSize = true,
            Text = logicalName,
            BackColor = System.Drawing.Color.FromArgb(232, 240, 254),
            ForeColor = System.Drawing.Color.FromArgb(32, 66, 120),
            BorderStyle = BorderStyle.FixedSingle,
            Font = _uiFont,
            Margin = new Padding(0, 2, 4, 2),
            Padding = new Padding(6, 2, 6, 2)
         };
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
                  _lblSelectedEntities.Text =
                     $"Selected ({_selectedEntities.Count}) | " +
                     $"Records: {total:N0}";
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
         _chkSelectRecommended.Enabled = _syncCts == null;
         _chkForceResync.Enabled = _syncCts == null;
         _chkAutoCreateRelatedRecords.Enabled = _syncCts == null;
         _chkShowHiddenEntities.Enabled = _syncCts == null;
      }

      private void LoadEntities(bool resetSelection = true)
      {
         if(_sourceProvider == null || _serviceProvider == null)
            return;

         _countCts?.Cancel();
         if (resetSelection)
         {
            _selectedEntities.Clear();
            _totalRecordsCount = 0;
            _chkSelectRecommended.Checked = false;
            UpdateSelectedEntitiesLabel();
         }

         ResetSyncProgress();
         _clbEntities.Items.Clear();
         _clbEntities.Items.Add("Loading entities...");
         _clbEntities.Enabled = false;

         var includeHidden = _chkShowHiddenEntities.Checked;

         WorkAsync(new WorkAsyncInfo
         {
            Message = "Fetching entities from source...",
            Work = (worker, args) =>
            {
               var entityService =
                      _serviceProvider.GetRequiredService<IEntityService>();

               args.Result = entityService
                      .GetMigrationEntitiesAsync(
                         _sourceProvider,
                         includeHidden
                      )
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
                  RemoveUnavailableSelectedEntities();
                  FilterEntities();
                  UpdateSelectedEntitiesLabel();
                  _rtbLogs.AppendText(
                     $"Loaded {entities.Count:N0} entities " +
                     $"(show hidden: {includeHidden}).\n"
                  );
               }
            }
         });
      }

      private void RemoveUnavailableSelectedEntities()
      {
         var availableEntities = _allEntities
            .Select(e => e.LogicalName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

         _selectedEntities.RemoveWhere(e => !availableEntities.Contains(e));
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
         _chkSelectRecommended.Enabled = false;
         _chkForceResync.Enabled = false;
         _chkAutoCreateRelatedRecords.Enabled = false;
         _chkShowHiddenEntities.Enabled = false;
         _clbEntities.Enabled = false;
         _rtbLogs.Clear();
         _prgSync.Value = 0;
         _lblSyncStatus.Text = "Preparing synchronization...";
         SaveAutoCreateRelatedRecordsSetting();

         if (_userService == null)
         {
            _userService = new UserService(
               _serviceProvider.GetRequiredService<ILogger>(),
               _sourceProvider,
               _targetProvider
            );
         }

         var syncCts = _syncCts;
         var progress = new Progress<Tuple<int, string>>(update =>
         {
            _prgSync.Value = update.Item1;
            _lblSyncStatus.Text = update.Item2;
         });

         var job = new SynchronizationJob(
            _serviceProvider,
            _sourceProvider,
            _targetProvider,
            _userService,
            selectedLogicalNames,
            (int)_totalRecordsCount,
            _chkForceResync.Checked,
            (percent, message) =>
               ((IProgress<Tuple<int, string>>)progress).Report(
                  Tuple.Create(percent, message)
               )
         );

         Task.Run(() =>
         {
            try
            {
               job.Run(syncCts.Token);
               return null;
            }
            catch (OperationCanceledException)
            {
               return null;
            }
         }).ContinueWith(task =>
         {
            var elapsed = SyncTimer.Format(job.SyncElapsed);

            _syncCts?.Dispose();
            _syncCts = null;
            _clbEntities.Enabled = true;
            UpdateSyncButtonState();

            if (task.IsFaulted && task.Exception != null)
            {
               var error = task.Exception.GetBaseException();
               _lblSyncStatus.Text = "Synchronization failed.";
               _rtbLogs.AppendText(
                  $"\n[ERROR] Sync failed after {elapsed}: " +
                  $"{error.Message}\n"
               );

               MessageBox.Show(
                  $"Sync failed after {elapsed}: {error.Message}",
                  "Error",
                  MessageBoxButtons.OK,
                  MessageBoxIcon.Error
               );
            }
            else if (syncCts.IsCancellationRequested)
            {
               _prgSync.Value = 0;
               _lblSyncStatus.Text = "Synchronization cancelled.";
               _rtbLogs.AppendText(
                  $"\n[INFO] Synchronization cancelled after {elapsed}.\n"
               );

               MessageBox.Show(
                  $"Synchronization cancelled after {elapsed}.",
                  "Cancelled",
                  MessageBoxButtons.OK,
                  MessageBoxIcon.Information
               );
            }
            else
            {
               _prgSync.Value = 100;
               _lblSyncStatus.Text =
                  $"Synchronization complete in {elapsed}.";
               _rtbLogs.AppendText(
                  $"\n[SUCCESS] Synchronization complete in {elapsed}!\n"
               );

               MessageBox.Show(
                  $"Synchronization completed successfully in {elapsed}.",
                  "Success",
                  MessageBoxButtons.OK,
                  MessageBoxIcon.Information
               );
            }

            _rtbLogs.ScrollToCaret();
         }, TaskScheduler.FromCurrentSynchronizationContext());
      }

      private void CancelSync_Click(object? sender, EventArgs e)
      {
         _syncCts?.Cancel();
         _btnCancelSync.Enabled = false;
         _lblSyncStatus.Text = "Cancelling synchronization...";
      }
   }
}
