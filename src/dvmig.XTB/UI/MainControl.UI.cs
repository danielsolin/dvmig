using System.Drawing;
using System.Windows.Forms;

namespace dvmig.XTB.UI
{
    public partial class MainControl
    {
        private const int _connectionButtonWidth = 385;
        private readonly Font _buttonFont = new("Segoe UI", 10F, FontStyle.Bold);
        private readonly Font _uiFont = new("Segoe UI", 9F);

        private Button _btnSelectSource = null!;
        private Button _btnSelectTarget = null!;
        private Button _btnInstallComponents = null!;
        private Button _btnClearSelectedEntities = null!;
        private Label _lblSelectedEntities = null!;
        private FlowLayoutPanel _selectedEntityChipsPanel = null!;
        private CheckBox _chkSelectRecommended = null!;
        private CheckBox _chkForceResync = null!;
        private CheckBox _chkAutoCreateRelatedRecords = null!;
        private Button _btnSync = null!;
        private Button _btnCancelSync = null!;
        private Label _lblSyncStatus = null!;
        private ProgressBar _prgSync = null!;
        private CheckedListBox _clbEntities = null!;
        private TextBox _txtSearch = null!;
        private CheckBox _chkShowHiddenEntities = null!;
        private RichTextBox _rtbLogs = null!;
        private SplitContainer _mainSplit = null!;

        private void InitializeUI()
        {
            var topPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 144,
                ColumnCount = 3,
                RowCount = 3,
                Padding = new Padding(12),
                Font = _uiFont
            };
            topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 400));
            topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 400));
            
            topPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            topPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            topPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

            _btnSelectSource = new Button
            {
                Text = "Source: Not Connected",
                Dock = DockStyle.Left,
                Width = _connectionButtonWidth,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.Red,
                Font = _buttonFont
            };
            _btnSelectSource.Click += OnSelectSourceClick;

            _btnSelectTarget = new Button
            {
                Text = "Target: Not Connected",
                Dock = DockStyle.Left,
                Width = _connectionButtonWidth,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.Red,
                Font = _buttonFont
            };
            _btnSelectTarget.Click += OnSelectTargetClick;

            _btnInstallComponents = new Button
            {
                Text = "Install Components on Target",
                Dock = DockStyle.Left,
                Enabled = false,
                Visible = false,
                Width = _connectionButtonWidth,
                Font = _buttonFont
            };
            _btnInstallComponents.Click += OnTargetComponentsActionClick;

            _btnSync = new Button
            {
                Text = "Run Synchronization",
                Dock = DockStyle.Fill,
                Enabled = false,
                BackColor = Color.LightGreen,
                UseVisualStyleBackColor = false,
                Font = _buttonFont
            };
            _btnSync.Click += RunSync_Click;

            _btnCancelSync = new Button
            {
                Text = "Cancel",
                Dock = DockStyle.Fill,
                Enabled = false,
                Font = _buttonFont
            };
            _btnCancelSync.Click += CancelSync_Click;

            _lblSelectedEntities = new Label
            {
               Text = "Selected: None",
               Dock = DockStyle.Fill,
               TextAlign = ContentAlignment.MiddleLeft,
               AutoEllipsis = true,
               Font = _uiFont
            };

            _btnClearSelectedEntities = new Button
            {
               Text = "Clear",
               AutoSize = true,
               AutoSizeMode = AutoSizeMode.GrowAndShrink,
               Dock = DockStyle.Right,
               Enabled = false,
               FlatStyle = FlatStyle.Flat,
               Font = _uiFont,
               Margin = new Padding(0),
               Padding = new Padding(6, 0, 6, 0),
               UseVisualStyleBackColor = true
            };
            _btnClearSelectedEntities.FlatAppearance.BorderSize = 0;
            _btnClearSelectedEntities.Click += OnClearSelectedEntitiesClick;

            _selectedEntityChipsPanel = new FlowLayoutPanel
            {
               Dock = DockStyle.Fill,
               AutoScroll = true,
               WrapContents = true,
               Margin = new Padding(0),
               Padding = new Padding(0)
            };

            _chkSelectRecommended = new CheckBox
            {
               Text = "Select recommended",
               AutoSize = true,
               Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
               Margin = new Padding(18, 0, 0, 0),
               TextAlign = ContentAlignment.MiddleLeft,
               Font = _uiFont
            };
            _chkSelectRecommended.CheckedChanged +=
               OnSelectRecommendedChanged;

            _chkForceResync = new CheckBox
            {
               Text = "Force re-sync",
               AutoSize = true,
               Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
               Margin = new Padding(2, 0, 0, 0),
               TextAlign = ContentAlignment.MiddleLeft,
               Font = _uiFont
            };
            _chkForceResync.CheckedChanged += OnSyncOptionsChanged;

            _chkAutoCreateRelatedRecords = new CheckBox
            {
               Text = "Auto-create related records",
               AutoSize = true,
               Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
               Margin = new Padding(18, 0, 0, 0),
               TextAlign = ContentAlignment.MiddleLeft,
               Font = _uiFont
            };
            _chkAutoCreateRelatedRecords.CheckedChanged +=
               OnAutoCreateRelatedRecordsChanged;

            _prgSync = new ProgressBar
            {
                Dock = DockStyle.Fill,
                Visible = true
            };

            _lblSyncStatus = new Label
            {
               Text = "Ready",
               Dock = DockStyle.Fill,
               TextAlign = ContentAlignment.MiddleLeft,
               AutoEllipsis = true,
               Font = _uiFont
            };

            var syncOptionsPanel = new FlowLayoutPanel
            {
               Dock = DockStyle.Fill,
               FlowDirection = FlowDirection.LeftToRight,
               WrapContents = false,
               Margin = new Padding(3, 0, 3, 0),
               Padding = new Padding(0, 5, 0, 0)
            };
            syncOptionsPanel.Controls.Add(_chkForceResync);
            syncOptionsPanel.Controls.Add(_chkAutoCreateRelatedRecords);

            var selectedEntitiesPanel = new TableLayoutPanel
            {
               Dock = DockStyle.Fill,
               ColumnCount = 1,
               RowCount = 1,
               Margin = new Padding(8, 0, 8, 0),
               Padding = new Padding(0, 2, 0, 0)
            };
            selectedEntitiesPanel.ColumnStyles.Add(
               new ColumnStyle(SizeType.Percent, 100)
            );
            selectedEntitiesPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            selectedEntitiesPanel.Controls.Add(_selectedEntityChipsPanel, 0, 0);

            topPanel.Controls.Add(_btnSelectSource, 0, 0);
            topPanel.Controls.Add(_btnSelectTarget, 0, 1);
            topPanel.Controls.Add(_btnInstallComponents, 0, 2);
            topPanel.Controls.Add(selectedEntitiesPanel, 1, 0);
            topPanel.SetRowSpan(selectedEntitiesPanel, 3);
            topPanel.Controls.Add(_btnSync, 2, 0);
            topPanel.Controls.Add(_btnCancelSync, 2, 1);
            topPanel.Controls.Add(syncOptionsPanel, 2, 2);

            _txtSearch = new TextBox
            {
                Dock = DockStyle.Top,
                Text = "Search entities...",
                ForeColor = Color.Gray,
                Font = _uiFont
            };

            _txtSearch.GotFocus += OnSearchGotFocus;
            _txtSearch.LostFocus += OnSearchLostFocus;
            _txtSearch.TextChanged += OnSearchTextChanged;

            _chkShowHiddenEntities = new CheckBox
            {
               Text = "Show hidden",
               AutoSize = true,
               Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
               Margin = new Padding(0),
               TextAlign = ContentAlignment.MiddleLeft,
               Font = _uiFont
            };
            _chkShowHiddenEntities.CheckedChanged +=
               OnShowHiddenEntitiesChanged;

            _clbEntities = new CheckedListBox
            {
                Dock = DockStyle.Fill,
                CheckOnClick = true,
                IntegralHeight = false,
                Font = _uiFont
            };
            _clbEntities.ItemCheck += OnEntityItemCheck;

            _rtbLogs = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.Black,
                ForeColor = Color.LightGreen,
                Font = new Font("Consolas", 12)
            };

            var entityFilterOptionsPanel = new FlowLayoutPanel
            {
               Dock = DockStyle.Fill,
               FlowDirection = FlowDirection.LeftToRight,
               WrapContents = false,
               Margin = new Padding(0),
               Padding = new Padding(6, 5, 0, 0)
            };
            entityFilterOptionsPanel.Controls.Add(_chkShowHiddenEntities);
            entityFilterOptionsPanel.Controls.Add(_chkSelectRecommended);

            var entitySelectionSummaryPanel = new TableLayoutPanel
            {
               Dock = DockStyle.Fill,
               ColumnCount = 2,
               RowCount = 1,
               Margin = new Padding(0),
               Padding = new Padding(0)
            };
            entitySelectionSummaryPanel.ColumnStyles.Add(
               new ColumnStyle(SizeType.Percent, 100)
            );
            entitySelectionSummaryPanel.ColumnStyles.Add(
               new ColumnStyle(SizeType.AutoSize)
            );
            entitySelectionSummaryPanel.RowStyles.Add(
               new RowStyle(SizeType.Percent, 100)
            );
            entitySelectionSummaryPanel.Controls.Add(_lblSelectedEntities, 0, 0);
            entitySelectionSummaryPanel.Controls.Add(
               _btnClearSelectedEntities,
               1,
               0
            );

            var leftPanel = new TableLayoutPanel
            {
               Dock = DockStyle.Fill,
               ColumnCount = 1,
               RowCount = 4,
               Margin = new Padding(0),
               Padding = new Padding(12, 0, 0, 0)
            };
            leftPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            leftPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            leftPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            leftPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            leftPanel.Controls.Add(entitySelectionSummaryPanel, 0, 0);
            leftPanel.Controls.Add(entityFilterOptionsPanel, 0, 1);
            leftPanel.Controls.Add(_txtSearch, 0, 2);
            leftPanel.Controls.Add(_clbEntities, 0, 3);

            _mainSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                Panel1MinSize = 320
            };
            _mainSplit.SizeChanged += OnMainSplitSizeChanged;
            _mainSplit.Panel1.Controls.Add(leftPanel);

            var syncStatusPanel = new TableLayoutPanel
            {
               Dock = DockStyle.Top,
               Height = 54,
               ColumnCount = 1,
               RowCount = 2,
               Margin = new Padding(0),
               Padding = new Padding(0, 0, 0, 6)
            };
            syncStatusPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            syncStatusPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            syncStatusPanel.Controls.Add(_lblSyncStatus, 0, 0);
            syncStatusPanel.Controls.Add(_prgSync, 0, 1);
            _mainSplit.Panel2.Controls.Add(_rtbLogs);
            _mainSplit.Panel2.Controls.Add(syncStatusPanel);

            var contentPanel = new Panel
            {
               Dock = DockStyle.Fill,
               Padding = new Padding(0, 0, 12, 12)
            };
            contentPanel.Controls.Add(_mainSplit);

            Controls.Add(contentPanel);
            Controls.Add(topPanel);
        }
    }
}
