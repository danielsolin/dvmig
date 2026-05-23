using System.Drawing;
using System.Windows.Forms;

namespace dvmig.XTB.UI
{
    public partial class MainControl
    {
        private const int _connectionButtonWidth = 385;
        private readonly Font _buttonFont = new("Segoe UI", 10F);
        private readonly Font _uiFont = new("Segoe UI", 9F);

        private Button _btnSelectSource = null!;
        private Button _btnSelectTarget = null!;
        private Label _lblSelectedEntities = null!;
        private FlowLayoutPanel _selectedEntityChipsPanel = null!;
        private CheckBox _chkSelectRecommended = null!;
        private CheckBox _chkForceResync = null!;
        private Button _btnSync = null!;
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
                Height = 150,
                ColumnCount = 2,
                RowCount = 3,
                Padding = new Padding(12),
                Font = _uiFont
            };
            topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
            topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            
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

            _btnSync = new Button
            {
                Text = "Run Synchronization",
                Dock = DockStyle.Fill,
                Enabled = false,
                BackColor = Color.LightGreen,
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

            _selectedEntityChipsPanel = new FlowLayoutPanel
            {
               Dock = DockStyle.Fill,
               AutoScroll = true,
               WrapContents = false,
               Margin = new Padding(0),
               Padding = new Padding(0)
            };

            _chkSelectRecommended = new CheckBox
            {
               Text = "Select recommended",
               AutoSize = true,
               Anchor = AnchorStyles.Left,
               TextAlign = ContentAlignment.MiddleLeft,
               Font = _uiFont
            };
            _chkSelectRecommended.CheckedChanged +=
               OnSelectRecommendedChanged;

            _chkForceResync = new CheckBox
            {
               Text = "Force re-sync",
               AutoSize = true,
               Anchor = AnchorStyles.Left,
               TextAlign = ContentAlignment.MiddleLeft,
               Font = _uiFont
            };
            _chkForceResync.CheckedChanged += OnSyncOptionsChanged;

            _prgSync = new ProgressBar
            {
                Dock = DockStyle.Fill,
                Visible = true
            };

            topPanel.Controls.Add(_btnSelectSource, 0, 0);
            topPanel.Controls.Add(_btnSelectTarget, 0, 1);
            var optionPanel = new TableLayoutPanel
            {
               Dock = DockStyle.Fill,
               ColumnCount = 1,
               RowCount = 1,
               Margin = new Padding(0),
               Padding = new Padding(0)
            };
            optionPanel.ColumnStyles.Add(
               new ColumnStyle(SizeType.Absolute, 130)
            );
            optionPanel.Controls.Add(_chkForceResync, 0, 0);
            topPanel.Controls.Add(optionPanel, 0, 2);
            topPanel.Controls.Add(_btnSync, 1, 1);
            topPanel.Controls.Add(_btnCancelSync, 1, 2);

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
                Font = new Font("Consolas", 10)
            };

            var leftPanel = new TableLayoutPanel
            {
               Dock = DockStyle.Fill,
               ColumnCount = 1,
               RowCount = 2,
               Margin = new Padding(0),
               Padding = new Padding(0)
            };
            leftPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            leftPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            leftPanel.Controls.Add(_txtSearch, 0, 0);
            leftPanel.Controls.Add(_clbEntities, 0, 1);

            _mainSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                Panel1MinSize = 320
            };
            _mainSplit.SizeChanged += OnMainSplitSizeChanged;
            _mainSplit.Panel1.Controls.Add(leftPanel);
            _mainSplit.Panel2.Controls.Add(_rtbLogs);

            var selectionPanel = new TableLayoutPanel
            {
               Dock = DockStyle.Top,
               Height = 58,
               ColumnCount = 2,
               RowCount = 2,
               Margin = new Padding(0),
               Padding = new Padding(4, 2, 4, 2)
            };
            selectionPanel.ColumnStyles.Add(
               new ColumnStyle(SizeType.Absolute, 200)
            );
            selectionPanel.ColumnStyles.Add(
               new ColumnStyle(SizeType.Absolute, 150)
            );
            selectionPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            selectionPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            selectionPanel.Controls.Add(_lblSelectedEntities, 0, 0);
            selectionPanel.Controls.Add(_chkSelectRecommended, 1, 0);
            selectionPanel.Controls.Add(_selectedEntityChipsPanel, 0, 1);
            selectionPanel.SetColumnSpan(_selectedEntityChipsPanel, 2);

            var bottomPanel = new Panel
            {
               Dock = DockStyle.Bottom,
               Height = 36,
               Padding = new Padding(12, 4, 12, 8)
            };
            bottomPanel.Controls.Add(_prgSync);

            Controls.Add(_mainSplit);
            Controls.Add(selectionPanel);
            Controls.Add(bottomPanel);
            Controls.Add(topPanel);
        }
    }
}
