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
            UpdateSyncButtonState();
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
         SaveSyncSettings();
         OnSyncOptionsChanged(sender, e);
      }

      private void OnMaxThreadsChanged(object? sender, EventArgs e)
      {
         SaveSyncSettings();
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
         _cmbMaxThreads.SelectedItem =
            SystemConstants.SyncSettings
               .ParallelismOptions
               .Contains(settings.MaxParallelism)
               ? settings.MaxParallelism
               : 5;
      }

      private void SaveSyncSettings()
      {
         if (_serviceProvider == null)
            return;

         var settingsService = _serviceProvider
            .GetRequiredService<ISettingsService>();
         var settings = settingsService.LoadSettings();
         settings.AutoCreateRelatedRecords =
            _chkAutoCreateRelatedRecords.Checked;
         if (_cmbMaxThreads.SelectedItem is int maxThreads)
            settings.MaxParallelism = maxThreads;
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
         UpdateSyncButtonState();
      }

      private void OnClearSelectedEntitiesClick(object? sender, EventArgs e)
      {
         if (_selectedEntities.Count == 0)
            return;

         _selectedEntities.Clear();
         _chkSelectRecommended.Checked = false;
         FilterEntities();
         UpdateSelectedEntitiesLabel();
         ResetSyncProgress();
         UpdateSyncButtonState();
      }

      private void UpdateSelectedEntitiesLabel()
      {
         if (_selectedEntities.Count == 0)
         {
            _lblSelectedEntities.Text = "Selected: None";
            _totalRecordsCount = 0;
            _btnClearSelectedEntities.Enabled = false;
            return;
         }

         _btnClearSelectedEntities.Enabled = _syncCts == null;
         _lblSelectedEntities.Text =
            $"Selected ({_selectedEntities.Count}) | Records: Counting...";
         
         CalculateTotalRecords();
      }

      private void ResetUserMappingsPanel()
      {
         _isMappingUsers = false;
         _dgvUserMappings.Rows.Clear();
         _dgvUserMappings.Enabled = false;
         _btnEditUserMappings.Enabled = false;
      }

      private void OnUserMappingsSelectionChanged(
         object? sender,
         EventArgs e
      )
      {
         ClearUserMappingsSelection();
      }

      private void ClearUserMappingsSelection()
      {
         if (_isClearingUserMappingSelection)
            return;

         _isClearingUserMappingSelection = true;

         try
         {
            _dgvUserMappings.ClearSelection();
            _dgvUserMappings.CurrentCell = null;
         }
         finally
         {
            _isClearingUserMappingSelection = false;
         }
      }

      private void OnUserMappingsGroupResize(object? sender, EventArgs e)
      {
         _btnEditUserMappings.Location = new System.Drawing.Point(
            Math.Max(
               0,
               _grpUserMappings.ClientSize.Width -
                  _btnEditUserMappings.Width - 8
            ),
            0
         );
         _btnEditUserMappings.BringToFront();
      }

      private void OnEditUserMappingsClick(object? sender, EventArgs e)
      {
         if (_userService == null ||
             _targetProvider == null ||
             _syncCts != null ||
             _isCheckingTargetComponents ||
             _isUpdatingTargetComponents ||
             _isMappingUsers ||
             _isLoadingEntities)
            return;

         var userService = _userService;
         var target = _targetProvider;

         WorkAsync(new WorkAsyncInfo
         {
            Message = "Opening user mappings editor...",
            Work = (worker, args) =>
            {
               var mappings = userService.GetMappingSummaryAsync()
                  .GetAwaiter()
                  .GetResult();
               var targetUsers = UserMappingsEditorForm
                  .LoadTargetUsersAsync(target)
                  .GetAwaiter()
                  .GetResult();

               args.Result = new UserMappingsEditorData(
                  mappings,
                  targetUsers
               );
            },
            PostWorkCallBack = (args) =>
            {
               if (args.Error != null)
               {
                  MessageBox.Show(
                     "Could not open user mappings editor: " +
                     args.Error.GetBaseException().Message,
                     "User Mappings Editor",
                     MessageBoxButtons.OK,
                     MessageBoxIcon.Error
                  );

                  return;
               }

               if (args.Result is not UserMappingsEditorData data)
                  return;

               using (var editor = new UserMappingsEditorForm(
                  userService,
                  data.Mappings,
                  data.TargetUsers
               ))
               {
                  editor.ShowDialog(this);
               }

               var updatedMappings = userService.GetMappingSummaryAsync()
                  .GetAwaiter()
                  .GetResult();
               UpdateUserMappingsPanel(updatedMappings);
            }
         });
      }

      private void StartUserMappingIfReady()
      {
         if (_sourceProvider == null ||
             _targetProvider == null ||
             _serviceProvider == null)
            return;

         _userMappingCts?.Cancel();
         _userMappingCts = new CancellationTokenSource();
         var token = _userMappingCts.Token;

         var source = _sourceProvider;
         var target = _targetProvider;
         var userService = new UserService(
            _serviceProvider.GetRequiredService<ILogger>(),
            source,
            target
         );
         _userService = userService;

         _isMappingUsers = true;
         _dgvUserMappings.Rows.Clear();
         _dgvUserMappings.Enabled = false;
         UpdateSyncButtonState();

         WorkAsync(new WorkAsyncInfo
         {
            Message = "Mapping users...",
            Work = (worker, args) =>
            {
               userService.MapAllSourceUsersAsync(token)
                  .GetAwaiter()
                  .GetResult();

               args.Result = userService.GetMappingSummaryAsync(token)
                  .GetAwaiter()
                  .GetResult();
            },
            PostWorkCallBack = (args) =>
            {
               if (!ReferenceEquals(source, _sourceProvider) ||
                   !ReferenceEquals(target, _targetProvider))
                  return;

               _isMappingUsers = false;

               if (args.Error != null)
               {
                  _dgvUserMappings.Rows.Clear();
                  _dgvUserMappings.Enabled = false;
                  _dgvUserMappings.Rows.Add(
                     args.Error.GetBaseException().Message,
                     string.Empty,
                     "Error"
                  );
                  UpdateSyncButtonState();

                  return;
               }

               if (args.Result is IReadOnlyCollection<UserMappingSummary>
                   mappings)
                  UpdateUserMappingsPanel(mappings);

               UpdateSyncButtonState();
            }
         });
      }

      private void UpdateUserMappingsPanel(
         IReadOnlyCollection<UserMappingSummary> mappings
      )
      {
         var humanMappings = mappings
            .Where(m => m.IsHuman)
            .OrderBy(m => m.SourceName)
            .ToList();

         _dgvUserMappings.Rows.Clear();

         if (humanMappings.Count == 0)
         {
            _dgvUserMappings.Enabled = false;
            _dgvUserMappings.Rows.Add(
               "No human users found.",
               string.Empty,
               string.Empty
            );

            return;
         }

         foreach (var mapping in humanMappings)
         {
            var rowIndex = _dgvUserMappings.Rows.Add(
               mapping.SourceName,
               mapping.TargetName,
               mapping.Status.ToString()
            );

            var row = _dgvUserMappings.Rows[rowIndex];
            row.Cells[2].Style.ForeColor =
               mapping.Status == UserMappingStatus.Unmapped
                  ? System.Drawing.Color.DarkGoldenrod
                  : System.Drawing.Color.DarkGreen;
         }

         _dgvUserMappings.Enabled = true;
         _btnEditUserMappings.Enabled = true;
         ClearUserMappingsSelection();
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
         var isSyncRunning = _syncCts != null;
         var hasBlockingOperation =
            isSyncRunning ||
            _isCheckingTargetComponents ||
            _isUpdatingTargetComponents ||
            _isMappingUsers ||
            _isLoadingEntities;

         var canInteract = !hasBlockingOperation;
         var canRunSync = _sourceProvider != null &&
            _targetProvider != null &&
            _targetComponentsReady == true &&
            _selectedEntities.Count > 0 &&
            canInteract;

         _btnSync.Enabled = canRunSync;
         _btnSync.BackColor = canRunSync
            ? System.Drawing.Color.FromArgb(198, 239, 206)
            : System.Drawing.SystemColors.Control;
         _btnSync.ForeColor = canRunSync
            ? System.Drawing.SystemColors.ControlText
            : System.Drawing.SystemColors.GrayText;

         _btnCancelSync.Enabled = isSyncRunning;
         _btnSelectSource.Enabled = canInteract;
         _btnSelectTarget.Enabled = canInteract;
         _chkSelectRecommended.Enabled = canInteract;
         _chkForceResync.Enabled = canInteract;
         _chkAutoCreateRelatedRecords.Enabled = canInteract;
         _cmbMaxThreads.Enabled = canInteract;
         _chkShowHiddenEntities.Enabled = canInteract;
         _txtSearch.Enabled = canInteract;
         _clbEntities.Enabled = canInteract;
         _btnEditUserMappings.Enabled = canInteract &&
            _userService != null &&
            _sourceProvider != null &&
            _targetProvider != null;
         _btnClearSelectedEntities.Enabled =
            canInteract && _selectedEntities.Count > 0;
         UpdateInstallComponentsButtonState();
      }

      private void UpdateInstallComponentsButtonState()
      {
         var shouldShow = _targetProvider != null &&
            _targetComponentsReady.HasValue;

         _btnInstallComponents.Text = _targetComponentsReady == true
            ? "Uninstall Components on Target"
            : "Install Components on Target";

         _btnInstallComponents.Visible = shouldShow;
         _btnInstallComponents.Enabled = shouldShow &&
            _syncCts == null &&
            !_isMappingUsers &&
            !_isLoadingEntities &&
            !_isCheckingTargetComponents &&
            !_isUpdatingTargetComponents;
      }

      private void CheckTargetComponents()
      {
         if (_targetProvider == null || _serviceProvider == null)
            return;

         var target = _targetProvider;
         var environmentService =
            _serviceProvider.GetRequiredService<IEnvironmentService>();

         _isCheckingTargetComponents = true;
         _targetComponentsReady = null;
         UpdateSyncButtonState();
         _rtbLogs.AppendText("Checking dvmig components on Target...\n");

         WorkAsync(new WorkAsyncInfo
         {
            Message = "Checking dvmig components on Target...",
            Work = (worker, args) =>
            {
               args.Result = environmentService
                  .ValidateTargetEnvironmentAsync(target)
                  .GetAwaiter()
                  .GetResult();
            },
            PostWorkCallBack = (args) =>
            {
               if (!ReferenceEquals(target, _targetProvider))
                  return;

               _isCheckingTargetComponents = false;

               if(args.Error != null)
               {
                  _targetComponentsReady = false;
                  _rtbLogs.AppendText(
                     "Could not validate dvmig components on Target: " +
                     $"{args.Error.GetBaseException().Message}\n"
                  );
               }
               else if(args.Result is bool isReady && isReady)
               {
                  _targetComponentsReady = true;
                  _rtbLogs.AppendText(
                     "Target has the required dvmig components.\n"
                  );
               }
               else
               {
                  _targetComponentsReady = false;
                  _rtbLogs.AppendText(
                     "Target is missing required dvmig components. " +
                     "Install them before running synchronization.\n"
                  );
               }

               UpdateSyncButtonState();
            }
         });
      }

      private void OnTargetComponentsActionClick(object? sender, EventArgs e)
      {
         if (_syncCts != null ||
             _isCheckingTargetComponents ||
             _isUpdatingTargetComponents ||
             _isMappingUsers ||
             _isLoadingEntities)
            return;

         if (_targetComponentsReady == true)
            UninstallTargetComponents();
         else
            InstallTargetComponents();
      }

      private void InstallTargetComponents()
      {
         if (_targetProvider == null || _serviceProvider == null)
            return;

         var target = _targetProvider;
         var environmentService =
            _serviceProvider.GetRequiredService<IEnvironmentService>();

         _isUpdatingTargetComponents = true;
         UpdateSyncButtonState();
         _rtbLogs.AppendText("Installing dvmig components on Target...\n");

         WorkAsync(new WorkAsyncInfo
         {
            Message = "Installing dvmig components on Target...",
            Work = (worker, args) =>
            {
               environmentService
                  .InstallComponentsAsync(target)
                  .GetAwaiter()
                  .GetResult();
            },
            PostWorkCallBack = (args) =>
            {
               if (!ReferenceEquals(target, _targetProvider))
                  return;

               _isUpdatingTargetComponents = false;

               if(args.Error != null)
               {
                  _targetComponentsReady = false;
                  var error = args.Error.GetBaseException();

                  _rtbLogs.AppendText(
                     $"Failed to install dvmig components: {error.Message}\n"
                  );

                  MessageBox.Show(
                     $"Failed to install dvmig components: {error.Message}",
                     "Installation Failed",
                     MessageBoxButtons.OK,
                     MessageBoxIcon.Error
                  );

                  UpdateSyncButtonState();
                  return;
               }

               _rtbLogs.AppendText(
                  "dvmig components installed. Validating Target...\n"
               );
               CheckTargetComponents();
            }
         });
      }

      private void UninstallTargetComponents()
      {
         if (_targetProvider == null || _serviceProvider == null)
            return;

         var result = MessageBox.Show(
            "Remove all dvmig system components from the Target " +
            "environment?",
            "Uninstall dvmig Components",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2
         );

         if (result != DialogResult.Yes)
            return;

         var target = _targetProvider;
         var environmentService =
            _serviceProvider.GetRequiredService<IEnvironmentService>();

         _isUpdatingTargetComponents = true;
         UpdateSyncButtonState();
         _rtbLogs.AppendText("Uninstalling dvmig components from Target...\n");

         WorkAsync(new WorkAsyncInfo
         {
            Message = "Uninstalling dvmig components from Target...",
            Work = (worker, args) =>
            {
               environmentService
                  .UninstallComponentsAsync(target)
                  .GetAwaiter()
                  .GetResult();
            },
            PostWorkCallBack = (args) =>
            {
               if (!ReferenceEquals(target, _targetProvider))
                  return;

               _isUpdatingTargetComponents = false;

               if(args.Error != null)
               {
                  _targetComponentsReady = true;
                  var error = args.Error.GetBaseException();

                  _rtbLogs.AppendText(
                     "Failed to uninstall dvmig components: " +
                     $"{error.Message}\n"
                  );

                  MessageBox.Show(
                     "Failed to uninstall dvmig components: " +
                     $"{error.Message}",
                     "Uninstallation Failed",
                     MessageBoxButtons.OK,
                     MessageBoxIcon.Error
                  );

                  UpdateSyncButtonState();
                  return;
               }

               _rtbLogs.AppendText(
                  "dvmig components uninstalled. Validating Target...\n"
               );
               CheckTargetComponents();
            }
         });
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
         _isLoadingEntities = true;
         UpdateSyncButtonState();
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
               _isLoadingEntities = false;
               _clbEntities.Enabled = true;
               if(args.Error != null)
               {
                  _rtbLogs.AppendText(
                         $"Error fetching entities: {args.Error.Message}\n"
                      );

                  UpdateSyncButtonState();
                  return;
               }

               if(args.Result is List<EntityMetadata> entities)
               {
                  _allEntities = entities;
                  RemoveUnavailableSelectedEntities();
                  FilterEntities();
                  UpdateSelectedEntitiesLabel();
                  UpdateSyncButtonState();
                  _rtbLogs.AppendText(
                     $"Loaded {entities.Count:N0} entities " +
                     $"(show hidden: {includeHidden}).\n"
                  );
               }

               UpdateSyncButtonState();
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
         if (_syncCts != null ||
             _isCheckingTargetComponents ||
             _isUpdatingTargetComponents ||
             _isMappingUsers ||
             _isLoadingEntities)
         {
            MessageBox.Show(
               "Please wait for the current operation to finish before " +
               "running synchronization.",
               "Operation In Progress",
               MessageBoxButtons.OK,
               MessageBoxIcon.Information
            );

            return;
         }

         if(_sourceProvider == null)
         {
            MessageBox.Show(
               "Please select a Source environment before running " +
               "synchronization.",
               "Source Not Selected",
               MessageBoxButtons.OK,
               MessageBoxIcon.Warning
            );

            return;
         }

         if(_targetProvider == null)
         {
            MessageBox.Show(
               "Please select a Target environment before running " +
               "synchronization.",
               "Target Not Selected",
               MessageBoxButtons.OK,
               MessageBoxIcon.Warning
            );

            return;
         }

         if(_targetComponentsReady == false)
         {
            MessageBox.Show(
               "Please install dvmig components on the Target environment " +
               "before running synchronization.",
               "Target Components Missing",
               MessageBoxButtons.OK,
               MessageBoxIcon.Warning
            );

            return;
         }

         if(_targetComponentsReady != true)
         {
            MessageBox.Show(
               "Please wait until dvmig has checked the Target " +
               "components.",
               "Target Components Not Checked",
               MessageBoxButtons.OK,
               MessageBoxIcon.Information
            );

            return;
         }

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
         _cmbMaxThreads.Enabled = false;
         _chkShowHiddenEntities.Enabled = false;
         _btnClearSelectedEntities.Enabled = false;
         _clbEntities.Enabled = false;
         _rtbLogs.Clear();
         _prgSync.Value = 0;
         _lblSyncStatus.Text = "Preparing synchronization...";
         SaveSyncSettings();

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
               ),
            mappings =>
            {
               if (IsDisposed || !IsHandleCreated)
                  return;

               BeginInvoke(new Action(() =>
                  UpdateUserMappingsPanel(mappings)
               ));
            }
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
               XTBLogWriter.AppendAccentMessage(
                  _rtbLogs,
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
