using System.Drawing;
using System.Windows.Forms;

namespace dvmig.XTB.UI
{
    public partial class MainControl
    {
        private Button _btnSelectSource = null!;
        private Button _btnSelectTarget = null!;
        private Label _lblSource = null!;
        private Label _lblTarget = null!;
        private Label _lblSelectedEntities = null!;
        private CheckBox _chkForceResync = null!;
        private Button _btnSync = null!;
        private Button _btnSelectRecommended = null!;
        private Button _btnCancelSync = null!;
        private ProgressBar _prgSync = null!;
        private CheckedListBox _clbEntities = null!;
        private TextBox _txtSearch = null!;
        private RichTextBox _rtbLogs = null!;
        private SplitContainer _mainSplit = null!;

        private void InitializeUI()
        {
            var topPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 205,
                ColumnCount = 2,
                RowCount = 5,
                Padding = new Padding(10)
            };
            topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
            topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            
            topPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            topPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            topPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            topPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            topPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

            _lblSource = new Label
            {
               Text = "Source: Not Connected",
               Dock = DockStyle.Fill,
               TextAlign = ContentAlignment.MiddleLeft,
               ForeColor = Color.Red
            };

            _btnSelectSource = new Button
            {
                Text = "Select Source",
                Dock = DockStyle.Fill
            };
            _btnSelectSource.Click += OnSelectSourceClick;

            _lblTarget = new Label
            {
               Text = "Target: Not Connected",
               Dock = DockStyle.Fill,
               TextAlign = ContentAlignment.MiddleLeft,
               ForeColor = Color.Red
            };

            _btnSelectTarget = new Button
            {
                Text = "Select Target",
                Dock = DockStyle.Fill
            };
            _btnSelectTarget.Click += OnSelectTargetClick;

            _btnSync = new Button
            {
                Text = "Run Synchronization",
                Dock = DockStyle.Fill,
                Enabled = false,
                BackColor = Color.LightGreen
            };
            _btnSync.Click += RunSync_Click;

            _btnSelectRecommended = new Button
            {
                Text = "Select Recommended",
                Dock = DockStyle.Fill
            };
            _btnSelectRecommended.Click += SelectRecommended_Click;

            _btnCancelSync = new Button
            {
                Text = "Cancel",
                Dock = DockStyle.Fill,
                Enabled = false
            };
            _btnCancelSync.Click += CancelSync_Click;

            _lblSelectedEntities = new Label
            {
               Text = "Selected: None",
               Dock = DockStyle.Fill,
               TextAlign = ContentAlignment.MiddleLeft,
               AutoEllipsis = true
            };

            _chkForceResync = new CheckBox
            {
               Text = "Force re-sync",
               Dock = DockStyle.Fill,
               TextAlign = ContentAlignment.MiddleLeft
            };
            _chkForceResync.CheckedChanged += OnSyncOptionsChanged;

            _prgSync = new ProgressBar
            {
                Dock = DockStyle.Fill,
                Visible = true
            };

            topPanel.Controls.Add(_lblSource, 0, 0);
            topPanel.Controls.Add(_btnSelectSource, 1, 0);
            topPanel.Controls.Add(_lblTarget, 0, 1);
            topPanel.Controls.Add(_btnSelectTarget, 1, 1);
            topPanel.Controls.Add(_lblSelectedEntities, 0, 2);
            topPanel.Controls.Add(_btnSync, 1, 2);
            topPanel.Controls.Add(_chkForceResync, 0, 3);
            var syncToolsPanel = new TableLayoutPanel
            {
               Dock = DockStyle.Fill,
               ColumnCount = 2,
               RowCount = 1,
               Margin = new Padding(0)
            };
            syncToolsPanel.ColumnStyles.Add(
               new ColumnStyle(SizeType.Percent, 60)
            );
            syncToolsPanel.ColumnStyles.Add(
               new ColumnStyle(SizeType.Percent, 40)
            );
            syncToolsPanel.Controls.Add(_btnSelectRecommended, 0, 0);
            syncToolsPanel.Controls.Add(_btnCancelSync, 1, 0);
            topPanel.Controls.Add(syncToolsPanel, 1, 3);
            topPanel.Controls.Add(_prgSync, 0, 4);
            topPanel.SetColumnSpan(_prgSync, 2);

            _txtSearch = new TextBox
            {
                Dock = DockStyle.Top,
                Text = "Search entities...",
                ForeColor = Color.Gray
            };

            _txtSearch.GotFocus += OnSearchGotFocus;
            _txtSearch.LostFocus += OnSearchLostFocus;
            _txtSearch.TextChanged += OnSearchTextChanged;

            _clbEntities = new CheckedListBox
            {
                Dock = DockStyle.Fill,
                CheckOnClick = true,
                IntegralHeight = false
            };
            _clbEntities.ItemCheck += OnEntityItemCheck;

            _rtbLogs = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.Black,
                ForeColor = Color.LightGreen,
                Font = new Font("Consolas", 10)
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
    }
}
