using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

using dvmig.Core.Interfaces;
using dvmig.Core.Shared;

namespace dvmig.XTB.UI
{
   internal sealed class UserMappingsEditorForm : Form
   {
      private readonly IUserService _userService;
      private readonly List<UserMappingSummary> _mappings;
      private readonly List<TargetUserOption> _targetUsers;
      private readonly DataGridView _grid;
      private readonly Button _btnApply;
      private readonly Button _btnOk;
      private readonly Button _btnCancel;

      public UserMappingsEditorForm(
         IUserService userService,
         IReadOnlyCollection<UserMappingSummary> mappings,
         IReadOnlyCollection<TargetUserOption> targetUsers
      )
      {
         _userService = userService;
         _mappings = mappings
            .Where(m => m.IsHuman)
            .OrderBy(m => m.SourceName)
            .ToList();
         _targetUsers = targetUsers
            .OrderBy(u => u.Name)
            .ToList();
         _targetUsers.Insert(0, new TargetUserOption(Guid.Empty, ""));

         foreach (var mapping in _mappings)
            EnsureTargetUserExists(mapping);

         Text = "User Mappings Editor";
         StartPosition = FormStartPosition.CenterParent;
         MinimizeBox = false;
         MaximizeBox = false;
         ShowInTaskbar = false;
         Size = new Size(760, 420);
         MinimumSize = new Size(640, 320);

         _grid = CreateGrid();
         _btnApply = CreateCommandButton("Apply");
         _btnOk = CreateCommandButton("OK");
         _btnCancel = CreateCommandButton("Cancel");

         _btnApply.Click += OnApplyClick;
         _btnOk.Click += OnOkClick;
         _btnCancel.Click += (_, _) => DialogResult = DialogResult.Cancel;

         var buttons = new FlowLayoutPanel
         {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 44,
            Padding = new Padding(8),
            WrapContents = false
         };
         buttons.Controls.Add(_btnCancel);
         buttons.Controls.Add(_btnOk);
         buttons.Controls.Add(_btnApply);

         Controls.Add(_grid);
         Controls.Add(buttons);

         PopulateRows();
      }

      public static async Task<List<TargetUserOption>> LoadTargetUsersAsync(
         IDataverseProvider targetProvider,
         CancellationToken ct = default
      )
      {
         var query = new QueryExpression(
            SystemConstants.DataverseEntities.SystemUser.Name
         )
         {
            ColumnSet = new ColumnSet(
               SystemConstants.DataverseAttributes.SystemUserId,
               SystemConstants.DataverseAttributes.FullName
            )
         };

         query.Criteria.AddCondition(
            SystemConstants.DataverseAttributes.IsDisabled,
            ConditionOperator.Equal,
            false
         );
         query.Criteria.AddCondition(
            SystemConstants.DataverseAttributes.AccessMode,
            ConditionOperator.Equal,
            0
         );

         var result = await targetProvider.RetrieveMultipleAsync(query, ct);

         return result.Entities
            .Select(e => new TargetUserOption(
               e.Id,
               e.GetAttributeValue<string>(
                  SystemConstants.DataverseAttributes.FullName
               ) ?? e.Id.ToString()
            ))
            .OrderBy(u => u.Name)
            .ToList();
      }

      private DataGridView CreateGrid()
      {
         var grid = new DataGridView
         {
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            ColumnHeadersHeightSizeMode =
               DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            Dock = DockStyle.Fill,
            MultiSelect = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect
         };

         grid.Columns.Add(new DataGridViewTextBoxColumn
         {
            Name = "SourceUser",
            HeaderText = "Source User",
            ReadOnly = true,
            FillWeight = 40
         });

         grid.Columns.Add(new DataGridViewComboBoxColumn
         {
            Name = "TargetUser",
            HeaderText = "Target User",
            DataSource = _targetUsers,
            DisplayMember = nameof(TargetUserOption.Name),
            ValueMember = nameof(TargetUserOption.Id),
            FlatStyle = FlatStyle.Flat,
            FillWeight = 44
         });

         grid.Columns.Add(new DataGridViewTextBoxColumn
         {
            Name = "Status",
            HeaderText = "Status",
            ReadOnly = true,
            FillWeight = 16
         });

         return grid;
      }

      private static Button CreateCommandButton(string text)
      {
         return new Button
         {
            Text = text,
            Width = 86,
            Height = 28,
            Margin = new Padding(6, 0, 0, 0)
         };
      }

      private void PopulateRows()
      {
         _grid.Rows.Clear();

         foreach (var mapping in _mappings)
         {
            var rowIndex = _grid.Rows.Add(
               mapping.SourceName,
               mapping.TargetId,
               mapping.Status.ToString()
            );

            _grid.Rows[rowIndex].Tag = mapping;
         }
      }

      private void EnsureTargetUserExists(UserMappingSummary mapping)
      {
         if (mapping.TargetId == Guid.Empty ||
             _targetUsers.Any(u => u.Id == mapping.TargetId))
            return;

         _targetUsers.Add(new TargetUserOption(
            mapping.TargetId,
            mapping.TargetName
         ));
      }

      private void OnApplyClick(object? sender, EventArgs e)
      {
         ApplyManualMappings();
      }

      private void OnOkClick(object? sender, EventArgs e)
      {
         ApplyManualMappings();
         DialogResult = DialogResult.OK;
      }

      private void ApplyManualMappings()
      {
         _grid.EndEdit();

         foreach (DataGridViewRow row in _grid.Rows)
         {
            if (row.Tag is not UserMappingSummary mapping)
               continue;

            var selectedTargetId = row.Cells["TargetUser"].Value;
            if (selectedTargetId is not Guid targetId ||
                targetId == Guid.Empty ||
                targetId == mapping.TargetId)
               continue;

            var targetName = _targetUsers
               .FirstOrDefault(u => u.Id == targetId)?
               .Name;

            _userService.AddManualMapping(
               mapping.SourceId,
               targetId,
               mapping.SourceName,
               targetName,
               mapping.IsHuman
            );

            row.Cells["Status"].Value = UserMappingStatus.Manual.ToString();
            row.Tag = new UserMappingSummary(
               mapping.SourceName,
               mapping.SourceId,
               targetName ?? targetId.ToString(),
               targetId,
               UserMappingStatus.Manual,
               mapping.IsHuman
            );
         }
      }
   }

   internal sealed class TargetUserOption
   {
      public TargetUserOption(Guid id, string name)
      {
         Id = id;
         Name = name;
      }

      public Guid Id { get; }
      public string Name { get; }
   }

   internal sealed class UserMappingsEditorData
   {
      public UserMappingsEditorData(
         List<UserMappingSummary> mappings,
         List<TargetUserOption> targetUsers
      )
      {
         Mappings = mappings;
         TargetUsers = targetUsers;
      }

      public List<UserMappingSummary> Mappings { get; }
      public List<TargetUserOption> TargetUsers { get; }
   }
}
