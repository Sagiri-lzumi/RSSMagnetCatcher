using System.ComponentModel;
using RSSMagnetCatcher.Core.Models;
using RSSMagnetCatcher.Core.Services;
using RSSMagnetCatcher.Core.Utils;
using RSSMagnetCatcher.Infrastructure;
using RSSMagnetCatcher.Storage;

namespace RSSMagnetCatcher.UI;

public sealed class MainForm : Form
{
    private readonly DataPaths _paths;
    private readonly JsonConfigStore _configStore;
    private readonly JsonlItemStore _itemStore;
    private readonly ExportHistoryStore _historyStore;
    private readonly FeedStateStore _feedStateStore;
    private readonly ClipboardExportService _clipboardExportService;
    private readonly TorrentExportService _torrentExportService;
    private readonly CurrentFilterService _currentFilterService;
    private readonly ItemFilterService _itemFilterService;
    private readonly ItemWorkflowService _itemWorkflowService;
    private readonly ItemListQueryService _itemListQueryService;
    private readonly FeedScheduler _scheduler;
    private readonly ApplicationStatusService _statusService;
    private readonly AppSettings _settings;
    private readonly CacheMaintenanceService _cacheMaintenanceService;
    private readonly FeedDiagnosticsService _diagnosticsService;
    private readonly RulePresetService _rulePresetService;
    private readonly RuleMatchService _ruleMatchService;
    private readonly StartupManager _startupManager;
    private readonly TreeView _sidebarTree = new();
    private readonly DataGridView _itemGrid = new();
    private readonly TextBox _filterTextBox = new();
    private readonly TextBox _searchTextBox = new();
    private readonly Label _filterErrorLabel = new();
    private readonly Label _scopeSummaryLabel = new();
    private readonly Label _conditionSummaryLabel = new();
    private readonly Label _batchSummaryLabel = new();
    private readonly Button _cancelBatchButton = new();
    private readonly CheckBox _searchGlobalCheckBox = new();
    private readonly CheckBox _searchIncludeDeletedCheckBox = new();
    private readonly ToolTip _toolTip = new();
    private readonly FlowLayoutPanel _presetQuickPanel = new() { AutoSize = true, WrapContents = true };
    private readonly ToolStripStatusLabel _stateStatusLabel = new();
    private readonly ToolStripStatusLabel _countsStatusLabel = new();
    private readonly ToolStripStatusLabel _searchStatusLabel = new();
    private readonly ToolStripStatusLabel _viewHintStatusLabel = new();
    private readonly ToolStripStatusLabel _nextCheckStatusLabel = new();
    private readonly List<ToolStripItem> _interactiveItems = [];
    private BindingList<MagnetItem> _visibleItems = [];
    private List<FeedConfig> _feeds = [];
    private bool _allowClose;

    public MainForm(
        DataPaths paths,
        JsonConfigStore configStore,
        JsonlItemStore itemStore,
        ExportHistoryStore historyStore,
        FeedStateStore feedStateStore,
        ClipboardExportService clipboardExportService,
        TorrentExportService torrentExportService,
        CurrentFilterService currentFilterService,
        ItemFilterService itemFilterService,
        ItemWorkflowService itemWorkflowService,
        ItemListQueryService itemListQueryService,
        FeedScheduler scheduler,
        ApplicationStatusService statusService,
        AppSettings settings,
        CacheMaintenanceService cacheMaintenanceService,
        FeedDiagnosticsService diagnosticsService,
        RulePresetService rulePresetService,
        RuleMatchService ruleMatchService,
        StartupManager startupManager)
    {
        _paths = paths;
        _configStore = configStore;
        _itemStore = itemStore;
        _historyStore = historyStore;
        _feedStateStore = feedStateStore;
        _clipboardExportService = clipboardExportService;
        _torrentExportService = torrentExportService;
        _currentFilterService = currentFilterService;
        _itemFilterService = itemFilterService;
        _itemWorkflowService = itemWorkflowService;
        _itemListQueryService = itemListQueryService;
        _scheduler = scheduler;
        _statusService = statusService;
        _settings = settings;
        _cacheMaintenanceService = cacheMaintenanceService;
        _diagnosticsService = diagnosticsService;
        _rulePresetService = rulePresetService;
        _ruleMatchService = ruleMatchService;
        _startupManager = startupManager;

        BuildInterface();
        UiTheme.Apply(this);
        ReloadPresetQuickButtons();
        LoadFeeds();
        _itemFilterService.ReevaluateAll();
        RefreshItems();
        UpdateConditionSummary();
        UpdateBatchSummary();

        _currentFilterService.Changed += (_, _) => RunOnUiThread(HandleFilterChanged);
        _scheduler.StateChanged += (_, _) => RunOnUiThread(UpdateStatus);
        _scheduler.RunCompleted += (_, eventArgs) => RunOnUiThread(() => HandleRunCompleted(eventArgs.Result));
    }

    public event EventHandler? StatusChanged;

    public event EventHandler? RulesChanged;

    public event EventHandler? ExitRequested;

    public ApplicationStatusSnapshot CurrentStatus { get; private set; } =
        new(ApplicationState.Normal, 0, 0, 0, null, 0, 0);

    public string CurrentFilterExpression => _currentFilterService.CurrentExpression;

    public void AllowApplicationClose()
    {
        _allowClose = true;
    }

    public void ShowFromTray(bool showUnexported = false)
    {
        if (showUnexported)
        {
            SelectView(ItemListViewMode.Pending);
        }

        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    public async Task CheckAllAsync()
    {
        var result = await _scheduler.CheckAllNowAsync(true);
        if (!result.Started)
        {
            MessageBox.Show(this, "RSS 检查正在进行中。", "立即检查", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    public async Task CheckFailedAsync()
    {
        var result = await _scheduler.CheckFailedNowAsync(true);
        if (!result.Started)
        {
            MessageBox.Show(
                this,
                "没有失败订阅，或 RSS 检查正在进行中。",
                "只检查失败订阅",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

    public async Task BackfillSelectedMikanHistoryAsync()
    {
        var feed = GetSelectedFeed();
        if (feed is null)
        {
            MessageBox.Show(this, "请先选择一个 RSS 订阅。", "补抓 Mikan 历史", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!MikanHistoryService.IsSupportedUrl(feed.Url))
        {
            MessageBox.Show(this, "当前订阅不是支持的 Mikan Classic 地址。", "补抓 Mikan 历史", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var result = await _scheduler.BackfillMikanHistoryNowAsync(feed);
        if (!result.Started)
        {
            MessageBox.Show(this, "RSS 检查正在进行中。", "补抓 Mikan 历史", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    public int CopyAllUnexported(bool showMessage)
    {
        return CopyItems(
            _clipboardExportService.SelectUnexported(_itemStore.LoadLatest()),
            "复制全部待导出磁力",
            showMessage,
            completeActiveBatch: false);
    }

    public int CopyMatchingUnexported(bool showMessage)
    {
        return CopyItems(
            _clipboardExportService.SelectMatching(
                _itemStore.LoadLatest(),
                _itemFilterService.IsMatched,
                true),
            "复制符合当前条件的待导出项",
            showMessage,
            completeActiveBatch: false);
    }

    public bool TryApplyFilter(string expression, bool showError)
    {
        if (_currentFilterService.TryApply(expression, out var error))
        {
            if (!string.Equals(_filterTextBox.Text, _currentFilterService.CurrentExpression, StringComparison.Ordinal))
            {
                _filterTextBox.Text = _currentFilterService.CurrentExpression;
            }

            _filterErrorLabel.Text = string.Empty;
            UpdateConditionSummary();
            return true;
        }

        _filterErrorLabel.Text = error;
        UpdateConditionSummary(error);
        if (showError)
        {
            MessageBox.Show(this, error, "条件表达式无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        return false;
    }

    public void AppendQuickFilter(FilterQuickOption option)
    {
        if (!_currentFilterService.TryAppend(option.Clause, out var error))
        {
            _filterErrorLabel.Text = error;
            MessageBox.Show(this, error, "条件表达式无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    public void ClearFilter()
    {
        TryApplyFilter(string.Empty, false);
    }

    public void ApplyPreset(FilterRule rule)
    {
        TryApplyFilter(_ruleMatchService.BuildExpression(rule), true);
    }

    public void OpenRulePicker()
    {
        using var form = new RulePickerForm(
            _currentFilterService.CurrentExpression,
            _rulePresetService,
            _ruleMatchService);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            TryApplyFilter(form.Expression, true);
        }

        if (form.PresetsChanged)
        {
            ReloadPresetQuickButtons();
            RulesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void OpenSettings()
    {
        using var form = new SettingsForm(
            _paths,
            _configStore,
            _settings,
            _rulePresetService,
            _ruleMatchService,
            _cacheMaintenanceService,
            _startupManager);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            RefreshItems();
        }

        if (form.RulesChanged)
        {
            ReloadPresetQuickButtons();
            RulesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void OpenDiagnostics()
    {
        using var form = new DiagnosticsForm(
            () => _configStore.Load(_paths.FeedsFile, new List<FeedConfig>()),
            _feedStateStore,
            _diagnosticsService);
        form.ShowDialog(this);
    }

    public void OpenLogs()
    {
        using var form = new LogViewerForm(_paths);
        form.ShowDialog(this);
    }

    public void OpenLogsDirectory()
    {
        PathLauncher.OpenDirectory(_paths.LogsDirectory);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            if (_settings.CloseWindowToTray)
            {
                Hide();
            }
            else
            {
                ExitRequested?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        base.OnFormClosing(e);
    }

    private void BuildInterface()
    {
        Text = "RSS Magnet Collector";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1080, 680);
        ClientSize = new Size(1360, 820);
        BackColor = UiTheme.WindowBackColor;

        var menu = BuildMenuStrip();
        var summaryBar = BuildSummaryBar();
        var filterBar = BuildFilterBar();
        var searchBar = BuildSearchBar();
        var actionBar = BuildActionBar();
        var statusStrip = BuildStatusStrip();

        ConfigureSidebar();
        ConfigureItemGrid();

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 250,
            FixedPanel = FixedPanel.Panel1
        };
        split.BackColor = UiTheme.WindowBackColor;
        split.Panel1.Padding = new Padding(16, 14, 10, 14);
        split.Panel2.Padding = new Padding(10, 14, 16, 14);
        split.Panel1.BackColor = UiTheme.PanelBackColor;
        split.Panel2.BackColor = UiTheme.PanelBackColor;
        split.Panel1.Controls.Add(_sidebarTree);
        split.Panel1.Controls.Add(CreateSectionLabel("订阅与视图"));
        split.Panel2.Controls.Add(_itemGrid);
        split.Panel2.Controls.Add(CreateSectionLabel("条目列表"));

        Controls.Add(split);
        Controls.Add(actionBar);
        Controls.Add(statusStrip);
        Controls.Add(searchBar);
        Controls.Add(filterBar);
        Controls.Add(summaryBar);
        Controls.Add(menu);
        MainMenuStrip = menu;
    }

    private MenuStrip BuildMenuStrip()
    {
        var menu = new MenuStrip();
        var feeds = new ToolStripMenuItem("订阅");
        feeds.DropDownItems.Add(CreateMenuItem("添加 RSS", AddFeed));
        feeds.DropDownItems.Add(CreateMenuItem("编辑选中 RSS", EditFeed));
        feeds.DropDownItems.Add(CreateMenuItem("删除选中 RSS", DeleteFeed));
        feeds.DropDownItems.Add(CreateMenuItem("启用 / 停用选中 RSS", ToggleFeed));
        feeds.DropDownItems.Add(new ToolStripSeparator());
        feeds.DropDownItems.Add(CreateMenuItem("补抓选中 Mikan 历史", () => _ = BackfillSelectedMikanHistoryAsync()));

        var checks = new ToolStripMenuItem("检查");
        checks.DropDownItems.Add(CreateMenuItem("立即检查全部订阅", () => _ = CheckAllAsync()));
        checks.DropDownItems.Add(CreateMenuItem("只检查失败订阅", () => _ = CheckFailedAsync()));

        var export = new ToolStripMenuItem("导出");
        export.DropDownItems.Add(CreateMenuItem("按条件筛选并勾选", CheckCurrentFilteredItems));
        export.DropDownItems.Add(CreateMenuItem("复制已勾选磁力", CopyCheckedItems));
        export.DropDownItems.Add(CreateMenuItem("导出已勾选种子", () => _ = ExportCheckedTorrentsAsync()));
        export.DropDownItems.Add(CreateMenuItem("清除当前勾选", ClearCurrentCheckedItems));
        export.DropDownItems.Add(CreateMenuItem("取消本次批选", CancelActiveBatch));
        export.DropDownItems.Add(new ToolStripSeparator());
        export.DropDownItems.Add(CreateMenuItem("复制全部待导出磁力", () => CopyAllUnexported(true)));
        export.DropDownItems.Add(CreateMenuItem("全部勾选当前范围", SelectAllCurrentItems));
        export.DropDownItems.Add(new ToolStripSeparator());
        export.DropDownItems.Add(CreateMenuItem("恢复选中暂不导出为待导出", RestoreSelectedDiscarded));
        export.DropDownItems.Add(CreateMenuItem("删除选中条目", SoftDeleteSelectedItems));

        var advancedExport = new ToolStripMenuItem("高级导出");
        advancedExport.DropDownItems.Add(CreateMenuItem("复制当前视图待导出磁力", CopyCurrentPendingItems));
        advancedExport.DropDownItems.Add(CreateMenuItem("复制当前订阅待导出磁力", CopyCurrentFeedItems));
        advancedExport.DropDownItems.Add(CreateMenuItem("复制当前条件待导出磁力", CopyCurrentFilterItems));
        advancedExport.DropDownItems.Add(CreateMenuItem("重新复制当前条件全部磁力（含已导出，可能重复）", RecopyCurrentFilterItems));
        advancedExport.DropDownItems.Add(CreateMenuItem("复制失败诊断", CopyFailureDiagnostics));
        export.DropDownItems.Add(new ToolStripSeparator());
        export.DropDownItems.Add(advancedExport);

        var tools = new ToolStripMenuItem("工具");
        tools.DropDownItems.Add(CreateMenuItem("条件选择", OpenRulePicker));
        tools.DropDownItems.Add(CreateMenuItem("诊断", OpenDiagnostics));
        tools.DropDownItems.Add(CreateMenuItem("日志", OpenLogs));
        tools.DropDownItems.Add(CreateMenuItem("设置", OpenSettings));

        menu.Items.AddRange([feeds, checks, export, tools]);
        return menu;
    }

    private Control BuildSummaryBar()
    {
        var container = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(18, 12, 18, 12),
            ColumnCount = 2,
            RowCount = 3,
            BackColor = UiTheme.PanelBackColor
        };
        container.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        container.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        container.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        container.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        container.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _scopeSummaryLabel.AutoSize = true;
        _scopeSummaryLabel.Anchor = AnchorStyles.Left;
        _scopeSummaryLabel.Font = new Font(Font.FontFamily, 10F, FontStyle.Bold);
        _scopeSummaryLabel.ForeColor = UiTheme.InkColor;
        _conditionSummaryLabel.AutoSize = true;
        _conditionSummaryLabel.Anchor = AnchorStyles.Left;
        _conditionSummaryLabel.Padding = new Padding(0, 4, 0, 0);
        _batchSummaryLabel.AutoSize = true;
        _batchSummaryLabel.Anchor = AnchorStyles.Left;
        _batchSummaryLabel.Padding = new Padding(0, 4, 0, 0);
        _batchSummaryLabel.ForeColor = UiTheme.AccentDarkColor;
        _cancelBatchButton.Text = "取消批选";
        _cancelBatchButton.AutoSize = true;
        _cancelBatchButton.Click += (_, _) => CancelActiveBatch();

        container.Controls.Add(_scopeSummaryLabel, 0, 0);
        container.Controls.Add(_cancelBatchButton, 1, 0);
        container.Controls.Add(_conditionSummaryLabel, 0, 1);
        container.SetColumnSpan(_conditionSummaryLabel, 2);
        container.Controls.Add(_batchSummaryLabel, 0, 2);
        container.SetColumnSpan(_batchSummaryLabel, 2);
        return container;
    }

    private Control BuildSearchBar()
    {
        var container = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(18, 0, 18, 12),
            ColumnCount = 6,
            RowCount = 1,
            BackColor = UiTheme.PanelBackColor
        };
        container.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        container.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        container.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        container.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        container.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        container.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var label = new Label
        {
            Text = "搜索：",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            ForeColor = UiTheme.InkColor
        };

        _searchTextBox.PlaceholderText = "搜索当前栏目，多个关键词用空格分隔";
        _searchTextBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _searchTextBox.Margin = new Padding(0, 0, 10, 0);
        _searchTextBox.TextChanged += (_, _) =>
        {
            if (!HasSearchText() && _searchIncludeDeletedCheckBox.Checked)
            {
                _searchIncludeDeletedCheckBox.Checked = false;
            }

            RefreshItems();
            UpdateSearchStatus();
        };
        _searchTextBox.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode == Keys.Enter)
            {
                RefreshItems();
                eventArgs.SuppressKeyPress = true;
            }
        };

        _searchGlobalCheckBox.Text = "全局";
        _searchGlobalCheckBox.AutoSize = true;
        _searchGlobalCheckBox.Anchor = AnchorStyles.Left;
        _searchGlobalCheckBox.CheckedChanged += (_, _) => RefreshItems();

        _searchIncludeDeletedCheckBox.Text = "含删除";
        _searchIncludeDeletedCheckBox.AutoSize = true;
        _searchIncludeDeletedCheckBox.Anchor = AnchorStyles.Left;
        _searchIncludeDeletedCheckBox.CheckedChanged += (_, _) => RefreshItems();

        var clearButton = new Button { Text = "清空", AutoSize = true };
        clearButton.Click += (_, _) =>
        {
            _searchTextBox.Clear();
            _searchGlobalCheckBox.Checked = false;
            _searchIncludeDeletedCheckBox.Checked = false;
            RefreshItems();
        };

        _toolTip.SetToolTip(_searchTextBox, "普通关键字搜索：空格分隔的多个词需要全部命中。");
        _toolTip.SetToolTip(_searchGlobalCheckBox, "搜索全部缓存条目，不受左侧栏目限制。");
        _toolTip.SetToolTip(_searchIncludeDeletedCheckBox, "仅在有搜索词时临时显示已删除条目。");
        _toolTip.SetToolTip(clearButton, "清空搜索并回到当前栏目。");

        container.Controls.Add(label, 0, 0);
        container.Controls.Add(_searchTextBox, 1, 0);
        container.Controls.Add(_searchGlobalCheckBox, 2, 0);
        container.Controls.Add(_searchIncludeDeletedCheckBox, 3, 0);
        container.Controls.Add(clearButton, 4, 0);
        return container;
    }

    private Control BuildActionBar()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 76,
            Padding = new Padding(18, 14, 18, 14),
            WrapContents = false,
            BackColor = UiTheme.PanelBackColor
        };
        bar.Controls.Add(CreateActionButton("刷新订阅", () => _ = CheckAllAsync(), "立即检查所有启用订阅。"));
        bar.Controls.Add(CreateActionButton("按条件筛选并勾选", CheckCurrentFilteredItems, "按当前条件自动勾选，之后可手动增减；复制或导出后结算批次。"));
        bar.Controls.Add(CreateActionButton("复制已勾选磁力", CopyCheckedItems, "复制已勾选条目的 magnet，并标记成功项为已导出。", primary: true));
        bar.Controls.Add(CreateActionButton("导出已勾选种子", () => _ = ExportCheckedTorrentsAsync(), "下载并保存已勾选条目的 torrent 文件，成功项标记为已导出。"));
        return bar;
    }

    private Button CreateActionButton(string text, Action action, string toolTip, bool primary = false)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = false,
            Width = text.Length > 8 ? 190 : 150,
            Height = 46,
            Margin = new Padding(0, 0, 10, 0),
            Font = new Font(Font.FontFamily, 9.5F, FontStyle.Bold),
            Tag = primary ? "primary" : "secondary"
        };
        if (primary)
        {
            UiTheme.StylePrimaryButton(button);
        }
        else
        {
            UiTheme.StyleSecondaryButton(button);
        }

        button.Click += (_, _) => action();
        _toolTip.SetToolTip(button, toolTip);
        return button;
    }

    private Control BuildFilterBar()
    {
        var container = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(18, 0, 18, 10),
            ColumnCount = 1,
            RowCount = 3,
            BackColor = UiTheme.PanelBackColor
        };
        var inputRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 5,
            RowCount = 1
        };
        inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var conditionLabel = new Label
        {
            Text = "条件：",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Font = new Font(Font.FontFamily, 9F, FontStyle.Bold)
        };
        _filterTextBox.Text = _currentFilterService.CurrentExpression;
        _filterTextBox.PlaceholderText = "例如：(GBK|CHS|简);(1080p|2160p);!(720p)";
        _filterTextBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _filterTextBox.Margin = new Padding(0, 0, 8, 0);
        _filterTextBox.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode == Keys.Enter)
            {
                TryApplyFilter(_filterTextBox.Text, true);
                eventArgs.SuppressKeyPress = true;
            }
        };

        var applyButton = new Button { Text = "应用条件", AutoSize = true };
        applyButton.Click += (_, _) => TryApplyFilter(_filterTextBox.Text, true);
        var pickerButton = new Button { Text = "条件选择", AutoSize = true };
        pickerButton.Click += (_, _) => OpenRulePicker();
        var clearButton = new Button { Text = "清空条件", AutoSize = true };
        clearButton.Click += (_, _) => ClearFilter();

        inputRow.Controls.Add(conditionLabel, 0, 0);
        inputRow.Controls.Add(_filterTextBox, 1, 0);
        inputRow.Controls.Add(applyButton, 2, 0);
        inputRow.Controls.Add(pickerButton, 3, 0);
        inputRow.Controls.Add(clearButton, 4, 0);

        var quickRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true
        };
        container.SizeChanged += (_, _) =>
        {
            quickRow.MaximumSize = new Size(
                Math.Max(320, container.ClientSize.Width - container.Padding.Horizontal),
                0);
        };
        foreach (var option in FilterQuickOption.All)
        {
            var button = new Button { Text = option.Label, AutoSize = true };
            button.Click += (_, _) => AppendQuickFilter(option);
            _toolTip.SetToolTip(button, $"追加条件：{option.Label}");
            quickRow.Controls.Add(button);
        }

        _presetQuickPanel.Margin = new Padding(8, 0, 0, 0);
        quickRow.Controls.Add(_presetQuickPanel);

        _filterErrorLabel.Dock = DockStyle.Fill;
        _filterErrorLabel.AutoSize = true;
        _filterErrorLabel.ForeColor = Color.Firebrick;
        _toolTip.SetToolTip(_filterTextBox, "条件用 ; 表示 AND，| 表示 OR，! 表示排除。回车应用。");
        _toolTip.SetToolTip(applyButton, "应用当前条件，不改变条目状态。");
        _toolTip.SetToolTip(pickerButton, "打开条件面板，组合常用语言、清晰度和排除规则。");
        _toolTip.SetToolTip(clearButton, "清空当前条件，恢复手动批选。");
        container.Controls.Add(inputRow, 0, 0);
        container.Controls.Add(quickRow, 0, 1);
        container.Controls.Add(_filterErrorLabel, 0, 2);
        return container;
    }

    private StatusStrip BuildStatusStrip()
    {
        var statusStrip = new StatusStrip();
        _stateStatusLabel.ForeColor = UiTheme.AccentDarkColor;
        _stateStatusLabel.Font = new Font(SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont, FontStyle.Bold);
        statusStrip.Items.Add(_stateStatusLabel);
        statusStrip.Items.Add(new ToolStripStatusLabel(" | "));
        statusStrip.Items.Add(_viewHintStatusLabel);
        statusStrip.Items.Add(new ToolStripStatusLabel { Spring = true });
        statusStrip.Items.Add(_searchStatusLabel);
        statusStrip.Items.Add(new ToolStripStatusLabel(" | "));
        statusStrip.Items.Add(_countsStatusLabel);
        statusStrip.Items.Add(new ToolStripStatusLabel(" | "));
        statusStrip.Items.Add(_nextCheckStatusLabel);
        return statusStrip;
    }

    private ToolStripMenuItem CreateMenuItem(string text, Action action)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) => action();
        _interactiveItems.Add(item);
        return item;
    }

    private void ReloadPresetQuickButtons()
    {
        _presetQuickPanel.Controls.Clear();
        foreach (var rule in _rulePresetService.Load().Where(rule => rule.Enabled && rule.ShowAsQuickButton))
        {
            var button = new Button { Text = $"预设：{rule.Name}", AutoSize = true };
            button.Click += (_, _) => ApplyPreset(rule);
            UiTheme.StyleSecondaryButton(button);
            _presetQuickPanel.Controls.Add(button);
        }
    }

    private void ConfigureSidebar()
    {
        _sidebarTree.Dock = DockStyle.Fill;
        _sidebarTree.BorderStyle = BorderStyle.None;
        _sidebarTree.HideSelection = false;
        _sidebarTree.FullRowSelect = true;
        _sidebarTree.ShowNodeToolTips = true;
        _sidebarTree.ItemHeight = 28;
        _sidebarTree.BackColor = UiTheme.PanelBackColor;
        _sidebarTree.ForeColor = UiTheme.InkColor;
        _sidebarTree.NodeMouseHover += (_, eventArgs) =>
        {
            if (eventArgs.Node?.Tag is FeedListEntry entry)
            {
                _viewHintStatusLabel.Text = GetEntryDescription(entry);
            }
        };
        _sidebarTree.AfterSelect += (_, _) => RefreshItems();
    }

    private static Label CreateSectionLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Top,
            ForeColor = UiTheme.InkColor,
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
            Padding = new Padding(0, 0, 0, 10),
            AutoSize = true
        };
    }

    private void ConfigureItemGrid()
    {
        _itemGrid.Dock = DockStyle.Fill;
        _itemGrid.AutoGenerateColumns = false;
        _itemGrid.AllowUserToAddRows = false;
        _itemGrid.AllowUserToDeleteRows = false;
        _itemGrid.RowHeadersVisible = false;
        _itemGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _itemGrid.BackgroundColor = Color.White;
        _itemGrid.BorderStyle = BorderStyle.None;
        _itemGrid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        _itemGrid.GridColor = UiTheme.HairlineColor;
        _itemGrid.EnableHeadersVisualStyles = false;
        _itemGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(232, 226, 213);
        _itemGrid.ColumnHeadersDefaultCellStyle.ForeColor = UiTheme.InkColor;
        _itemGrid.ColumnHeadersDefaultCellStyle.Font = new Font(Font.FontFamily, 9F, FontStyle.Bold);
        _itemGrid.ColumnHeadersHeight = 38;
        _itemGrid.RowTemplate.Height = 34;
        _itemGrid.AlternatingRowsDefaultCellStyle.BackColor = UiTheme.AlternateRowColor;
        _itemGrid.DefaultCellStyle.SelectionBackColor = UiTheme.SelectedRowColor;
        _itemGrid.DefaultCellStyle.SelectionForeColor = UiTheme.InkColor;
        _itemGrid.DefaultCellStyle.ForeColor = UiTheme.InkColor;
        _itemGrid.Columns.Add(CreateTextColumn("Indicator", "●", 44));
        _itemGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            DataPropertyName = nameof(MagnetItem.IsChecked),
            HeaderText = "勾选",
            Width = 50
        });
        _itemGrid.Columns.Add(CreateTextColumn(nameof(MagnetItem.IsNew), "NEW", 55));
        _itemGrid.Columns.Add(CreateTextColumn(nameof(MagnetItem.Title), "标题", 260, true));
        _itemGrid.Columns.Add(CreateTextColumn("FeedName", "订阅源", 140));
        _itemGrid.Columns.Add(CreateTextColumn("MagnetReady", "磁力", 72));
        _itemGrid.Columns.Add(CreateTextColumn("TorrentReady", "种子", 72));
        _itemGrid.Columns.Add(CreateTextColumn("Status", "提取状态", 140));
        _itemGrid.Columns.Add(CreateTextColumn(nameof(MagnetItem.PublishedAt), "发布时间", 155));
        _itemGrid.Columns.Add(CreateTextColumn(nameof(MagnetItem.FoundAt), "发现时间", 155));
        _itemGrid.ContextMenuStrip = BuildGridContextMenu();

        _itemGrid.CellFormatting += ItemGridOnCellFormatting;
        _itemGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_itemGrid.IsCurrentCellDirty)
            {
                _itemGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        _itemGrid.CellBeginEdit += ItemGridOnCellBeginEdit;
        _itemGrid.CellValueChanged += ItemGridOnCellValueChanged;
    }

    private ContextMenuStrip BuildGridContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("复制选中磁力", null, (_, _) => CopySelectedRows());
        menu.Items.Add("导出选中种子", null, (_, _) => _ = ExportSelectedTorrentsAsync());
        menu.Items.Add("恢复选中暂不导出为待导出", null, (_, _) => RestoreSelectedDiscarded());
        menu.Items.Add("删除选中条目", null, (_, _) => SoftDeleteSelectedItems());
        return menu;
    }

    private static DataGridViewTextBoxColumn CreateTextColumn(
        string name,
        string header,
        int width,
        bool fill = false)
    {
        return new DataGridViewTextBoxColumn
        {
            Name = name,
            DataPropertyName = name,
            HeaderText = header,
            Width = width,
            AutoSizeMode = fill ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.NotSet,
            ReadOnly = true
        };
    }

    private void LoadFeeds(string? selectFeedId = null)
    {
        _feeds = _configStore.Load(_paths.FeedsFile, new List<FeedConfig>());
        _sidebarTree.BeginUpdate();
        _sidebarTree.Nodes.Clear();

        var workspaceRoot = new TreeNode("工作区");
        workspaceRoot.Nodes.Add(CreateSidebarNode(new FeedListEntry(ItemListViewMode.Pending, null, "待导出")));
        workspaceRoot.Nodes.Add(CreateSidebarNode(new FeedListEntry(ItemListViewMode.Discarded, null, "暂不导出")));
        workspaceRoot.Nodes.Add(CreateSidebarNode(new FeedListEntry(ItemListViewMode.Used, null, "已导出")));
        workspaceRoot.Nodes.Add(CreateSidebarNode(new FeedListEntry(ItemListViewMode.Exceptions, null, "异常条目")));
        workspaceRoot.Nodes.Add(CreateSidebarNode(new FeedListEntry(ItemListViewMode.Failed, null, "检查失败")));
        _sidebarTree.Nodes.Add(workspaceRoot);

        var feedRoot = new TreeNode("RSS 订阅");
        foreach (var feed in _feeds)
        {
            var enabled = feed.Enabled ? string.Empty : "[停用] ";
            feedRoot.Nodes.Add(CreateSidebarNode(new FeedListEntry(ItemListViewMode.Feed, feed.Id, $"{enabled}[{feed.Group}] {feed.Name}")));
        }

        _sidebarTree.Nodes.Add(feedRoot);
        workspaceRoot.Expand();
        feedRoot.Expand();
        _sidebarTree.SelectedNode = selectFeedId is null
            ? workspaceRoot.Nodes[0]
            : FindSidebarNode(node =>
                node.Tag is FeedListEntry { FeedId: not null } entry
                && string.Equals(entry.FeedId, selectFeedId, StringComparison.Ordinal))
                ?? workspaceRoot.Nodes[0];
        _sidebarTree.EndUpdate();
    }

    private void RefreshItems()
    {
        var selected = GetSelectedEntry()
            ?? new FeedListEntry(ItemListViewMode.Pending, null, "待导出");
        var hasSearch = HasSearchText();
        var failedFeedIds = _feedStateStore.Load()
            .Where(pair => pair.Value.LastStatus == "failed")
            .Select(pair => pair.Key)
            .ToHashSet(StringComparer.Ordinal);
        var feedNamesById = _feeds.ToDictionary(feed => feed.Id, feed => feed.Name, StringComparer.Ordinal);

        var items = _itemListQueryService.Query(
            _itemStore.LoadLatest(),
            new ItemListQuery(
                selected.Mode,
                selected.FeedId,
                failedFeedIds,
                false,
                false,
                _searchTextBox.Text,
                _searchGlobalCheckBox.Checked ? ItemSearchScope.AllItems : ItemSearchScope.CurrentView,
                _searchIncludeDeletedCheckBox.Checked,
                feedNamesById),
            _itemFilterService.IsMatched);

        _visibleItems = new BindingList<MagnetItem>(items.ToList());
        _itemGrid.DataSource = _visibleItems;
        _searchIncludeDeletedCheckBox.Enabled = hasSearch;
        UpdateScopeSummary(selected);
        UpdateViewHint(selected);
        UpdateSearchStatus();
        UpdateStatus();
        UpdateBatchSummary();
    }

    private void SelectView(ItemListViewMode mode)
    {
        var node = FindSidebarNode(treeNode =>
            treeNode.Tag is FeedListEntry entry && entry.Mode == mode);
        if (node is not null)
        {
            _sidebarTree.SelectedNode = node;
        }
    }

    private bool HasSearchText()
    {
        return !string.IsNullOrWhiteSpace(_searchTextBox.Text);
    }

    private void UpdateSearchStatus()
    {
        if (!HasSearchText())
        {
            _searchStatusLabel.Text = "搜索：未启用";
            return;
        }

        var scope = _searchGlobalCheckBox.Checked ? "全局" : "当前栏目";
        var deleted = _searchIncludeDeletedCheckBox.Checked ? "｜含删除" : string.Empty;
        _searchStatusLabel.Text = $"搜索：{scope}{deleted}｜命中 {_visibleItems.Count} / 可见 {_visibleItems.Count}";
    }

    private void UpdateScopeSummary(FeedListEntry selected)
    {
        var exportableCount = _visibleItems.Count(_itemWorkflowService.IsExportable);
        var pendingCount = _visibleItems.Count(_itemWorkflowService.IsPending);
        var matchedCount = _visibleItems.Count(item => _itemWorkflowService.IsExportable(item) && _itemFilterService.IsMatched(item));
        var checkedCount = _visibleItems.Count(item => item.IsChecked && _itemWorkflowService.IsExportable(item));
        _scopeSummaryLabel.Text =
            $"当前范围：{selected.Name} | 可见 {_visibleItems.Count} | 可导出 {exportableCount} | "
            + $"待导出 {pendingCount} | 符合条件 {matchedCount} | 已勾选 {checkedCount}";
    }

    private void UpdateViewHint(FeedListEntry selected)
    {
        _viewHintStatusLabel.Text = GetEntryDescription(selected);
    }

    private void AddFeed()
    {
        using var form = new FeedEditForm(_rulePresetService.Load());
        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var feed = form.Feed;
        if (_feeds.Any(existing => string.Equals(existing.Url, feed.Url, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "该 RSS 地址已存在。", "无法添加订阅", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        feed.Id = $"feed_{HashHelper.Sha256Hex(feed.Url)[..12]}";
        _feeds.Add(feed);
        _configStore.Save(_paths.FeedsFile, _feeds);
        LoadFeeds(feed.Id);
        RefreshItems();
    }

    private void EditFeed()
    {
        var existing = GetSelectedFeed();
        if (existing is null)
        {
            MessageBox.Show(this, "请先选择一个 RSS 订阅。", "编辑 RSS", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var form = new FeedEditForm(_rulePresetService.Load(), existing);
        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var updated = form.Feed;
        if (_feeds.Any(feed =>
            !string.Equals(feed.Id, updated.Id, StringComparison.Ordinal)
            && string.Equals(feed.Url, updated.Url, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "该 RSS 地址已存在。", "无法保存订阅", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var index = _feeds.FindIndex(feed => string.Equals(feed.Id, updated.Id, StringComparison.Ordinal));
        if (index >= 0)
        {
            _feeds[index] = updated;
            SaveFeeds(updated.Id);
        }
    }

    private void DeleteFeed()
    {
        var feed = GetSelectedFeed();
        if (feed is null
            || MessageBox.Show(
                this,
                $"确定删除订阅“{feed.Name}”吗？历史条目会保留。",
                "删除 RSS",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        _feeds.RemoveAll(existing => string.Equals(existing.Id, feed.Id, StringComparison.Ordinal));
        _feedStateStore.Remove(feed.Id);
        SaveFeeds();
    }

    private void ToggleFeed()
    {
        var feed = GetSelectedFeed();
        if (feed is null)
        {
            MessageBox.Show(this, "请先选择一个 RSS 订阅。", "启用 / 停用", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        feed.Enabled = !feed.Enabled;
        SaveFeeds(feed.Id);
    }

    private void SaveFeeds(string? selectedFeedId = null)
    {
        _configStore.Save(_paths.FeedsFile, _feeds);
        LoadFeeds(selectedFeedId);
        RefreshItems();
    }

    private FeedConfig? GetSelectedFeed()
    {
        if (GetSelectedEntry() is not FeedListEntry { Mode: ItemListViewMode.Feed, FeedId: not null } selected)
        {
            return null;
        }

        return _feeds.FirstOrDefault(feed => string.Equals(feed.Id, selected.FeedId, StringComparison.Ordinal));
    }

    private FeedListEntry? GetSelectedEntry()
    {
        return _sidebarTree.SelectedNode?.Tag as FeedListEntry;
    }

    private TreeNode CreateSidebarNode(FeedListEntry entry)
    {
        return new TreeNode(entry.Name)
        {
            Tag = entry,
            ToolTipText = GetEntryDescription(entry)
        };
    }

    private static string GetEntryDescription(FeedListEntry entry)
    {
        return entry.Mode switch
        {
            ItemListViewMode.Pending => "待导出：还没导出，可复制磁力或导出种子。",
            ItemListViewMode.Discarded => "暂不导出：批选后未采用，可恢复后再处理。",
            ItemListViewMode.Used => "已导出：已经复制磁力或导出种子，仍可查看和重新复制。",
            ItemListViewMode.Exceptions => "异常条目：缺 magnet/种子或解析状态需要留意。",
            ItemListViewMode.Failed => "检查失败：最近检查失败的订阅条目。",
            ItemListViewMode.Feed => "订阅视图：只显示该订阅当前待导出的可导出条目。",
            _ => entry.Name
        };
    }

    private TreeNode? FindSidebarNode(Func<TreeNode, bool> predicate)
    {
        foreach (TreeNode root in _sidebarTree.Nodes)
        {
            var found = FindSidebarNode(root, predicate);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static TreeNode? FindSidebarNode(TreeNode node, Func<TreeNode, bool> predicate)
    {
        if (predicate(node))
        {
            return node;
        }

        foreach (TreeNode child in node.Nodes)
        {
            var found = FindSidebarNode(child, predicate);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private void CopyCheckedItems()
    {
        _itemGrid.EndEdit();
        CopyItems(
            _visibleItems.Where(item => item.IsChecked && _itemWorkflowService.IsCopyable(item)),
            "复制已勾选磁力",
            true,
            completeActiveBatch: true);
    }

    private async Task ExportCheckedTorrentsAsync()
    {
        _itemGrid.EndEdit();
        await ExportTorrentItemsAsync(
            _visibleItems.Where(item => item.IsChecked && _itemWorkflowService.IsExportable(item)),
            "导出勾选种子",
            completeActiveBatch: true);
    }

    private async Task ExportSelectedTorrentsAsync()
    {
        await ExportTorrentItemsAsync(
            GetSelectedGridItems().Where(_itemWorkflowService.IsExportable),
            "导出选中种子",
            completeActiveBatch: false);
    }

    private void SelectAllCurrentItems()
    {
        StartBatch(useCurrentRule: false, title: "全部勾选当前范围");
    }

    private void CheckCurrentFilteredItems()
    {
        StartBatch(useCurrentRule: true, title: "按条件筛选并勾选");
    }

    private void StartBatch(bool useCurrentRule, string title)
    {
        var selected = GetSelectedEntry()
            ?? new FeedListEntry(ItemListViewMode.Pending, null, "待导出");
        var result = _itemWorkflowService.StartBatch(
            _visibleItems,
            selected.Mode,
            selected.FeedId,
            useCurrentRule);
        RefreshItems();
        MessageBox.Show(
            this,
            result.Message,
            title,
            MessageBoxButtons.OK,
            result.Started ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private void ClearCurrentCheckedItems()
    {
        var count = _itemWorkflowService.ClearChecked(_visibleItems);
        RefreshItems();
        MessageBox.Show(
            this,
            count > 0 ? $"已取消 {count} 条勾选。" : "当前列表没有已勾选条目。",
            "清除当前勾选",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void CancelActiveBatch()
    {
        var batch = _itemWorkflowService.ActiveBatch;
        if (!batch.IsActive)
        {
            MessageBox.Show(this, "当前没有待结算批次。", "取消批选", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var count = _itemWorkflowService.CancelActiveBatch();
        RefreshItems();
        MessageBox.Show(this, $"已取消本次批选，并恢复 {count} 条勾选状态。", "取消批选", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void CopyCurrentFilterItems()
    {
        CopyItems(
            _clipboardExportService.SelectMatching(_visibleItems, _itemFilterService.IsMatched, true),
            "复制当前条件待导出磁力",
            true,
            completeActiveBatch: false);
    }

    private void RecopyCurrentFilterItems()
    {
        CopyItems(
            _clipboardExportService.SelectMatching(_visibleItems, _itemFilterService.IsMatched, false),
            "重新复制当前条件全部磁力（含已导出）",
            true,
            completeActiveBatch: false);
    }

    private void CopyCurrentPendingItems()
    {
        CopyItems(
            _clipboardExportService.SelectUnexported(_visibleItems),
            "复制当前视图待导出磁力",
            true,
            completeActiveBatch: false);
    }

    private void CopyCurrentFeedItems()
    {
        var feed = GetSelectedFeed();
        if (feed is null)
        {
            MessageBox.Show(this, "请先选择一个 RSS 订阅。", "复制当前订阅待导出磁力", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        CopyItems(
            _clipboardExportService.SelectMatching(
                _itemStore.LoadLatest().Where(item => string.Equals(item.FeedId, feed.Id, StringComparison.Ordinal)),
                _itemFilterService.IsMatched,
                true),
            "复制当前订阅待导出磁力",
            true,
            completeActiveBatch: false);
    }

    private void CopyFailureDiagnostics()
    {
        var report = _diagnosticsService.BuildFailureReport(_feeds, _feedStateStore.Load());
        if (string.IsNullOrWhiteSpace(report))
        {
            MessageBox.Show(this, "当前没有失败订阅。", "复制失败诊断", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Clipboard.SetText(report);
        MessageBox.Show(this, "失败诊断已复制到剪贴板。", "复制完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void CopySelectedRows()
    {
        CopyItems(GetSelectedGridItems().Where(_itemWorkflowService.IsCopyable), "复制选中磁力", true, completeActiveBatch: false);
    }

    private void RestoreSelectedDiscarded()
    {
        var count = _itemWorkflowService.RestoreDiscarded(GetSelectedGridItems());
        RefreshItems();
        MessageBox.Show(
            this,
            count > 0 ? $"已恢复 {count} 条为待导出。" : "没有可恢复的暂不导出条目。",
            "恢复暂不导出",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void SoftDeleteSelectedItems()
    {
        var selected = GetSelectedGridItems().ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "请先选择条目。", "删除条目", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (MessageBox.Show(
            this,
            $"确定删除选中的 {selected.Count} 条吗？删除后会从普通界面隐藏。",
            "删除条目",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        var count = _itemWorkflowService.SoftDelete(selected);
        RefreshItems();
        MessageBox.Show(this, $"已删除 {count} 条。", "删除条目", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private IReadOnlyList<MagnetItem> GetSelectedGridItems()
    {
        var rows = _itemGrid.SelectedRows.Cast<DataGridViewRow>().ToList();
        if (rows.Count == 0 && _itemGrid.CurrentRow is not null)
        {
            rows.Add(_itemGrid.CurrentRow);
        }

        return rows
            .Select(row => row.DataBoundItem)
            .OfType<MagnetItem>()
            .DistinctBy(item => item.Id)
            .ToList();
    }

    private int CopyItems(
        IEnumerable<MagnetItem> sourceItems,
        string title,
        bool showMessage,
        bool completeActiveBatch)
    {
        var items = sourceItems.ToList();
        if (items.Count == 0)
        {
            if (showMessage)
            {
                MessageBox.Show(this, "没有可复制的 magnet。", title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            return 0;
        }

        if (_itemWorkflowService.ActiveBatch.IsActive && !completeActiveBatch)
        {
            if (showMessage)
            {
                MessageBox.Show(this, "已有待结算批次，请先复制磁力、导出种子或取消本次批选。", title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            return 0;
        }

        try
        {
            var copiedMagnetCount = _clipboardExportService.CopyToClipboard(items);
            var now = DateTimeOffset.Now;
            foreach (var item in items)
            {
                _historyStore.Append(new ExportHistoryEntry
                {
                    ItemId = item.Id,
                    InfoHash = item.InfoHash,
                    Magnet = item.Magnet,
                    ExportKind = ExportKinds.Magnet,
                    ExportedAt = now
                });
            }

            var completion = _itemWorkflowService.CompleteCopy(items);

            _cacheMaintenanceService.Compact();
            RefreshItems();
            if (showMessage)
            {
                var discarded = completion.DiscardedCount > 0
                    ? $"；本批剩余 {completion.DiscardedCount} 条已转为暂不导出"
                    : string.Empty;
                var deduped = copiedMagnetCount == items.Count
                    ? string.Empty
                    : $"（已按 info hash 去重，勾选 {items.Count} 条）";
                MessageBox.Show(
                    this,
                    $"已复制 {copiedMagnetCount} 条 magnet 到剪贴板{deduped}{discarded}。",
                    title,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }

            return copiedMagnetCount;
        }
        catch (Exception exception)
        {
            if (showMessage)
            {
                MessageBox.Show(this, exception.Message, "复制失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return 0;
        }
    }

    private async Task<int> ExportTorrentItemsAsync(
        IEnumerable<MagnetItem> sourceItems,
        string title,
        bool completeActiveBatch)
    {
        var items = sourceItems.ToList();
        if (items.Count == 0)
        {
            MessageBox.Show(this, "请先勾选或选择可导出的条目。", title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        if (_itemWorkflowService.ActiveBatch.IsActive && !completeActiveBatch)
        {
            MessageBox.Show(this, "已有待结算批次，请先复制磁力、导出种子或取消本次批选。", title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 0;
        }

        try
        {
            var result = await _torrentExportService.ExportAsync(items);
            var successful = result.Results.Where(item => item.Succeeded).ToList();
            var unsuccessfulIds = result.Results
                .Where(item => !item.Succeeded)
                .Select(item => item.Item.Id)
                .ToHashSet(StringComparer.Ordinal);

            var now = DateTimeOffset.Now;
            foreach (var saved in successful)
            {
                _historyStore.Append(new ExportHistoryEntry
                {
                    ItemId = saved.Item.Id,
                    InfoHash = saved.Item.InfoHash,
                    Magnet = saved.Item.Magnet,
                    ExportKind = ExportKinds.Torrent,
                    TorrentUrl = saved.Item.TorrentUrl,
                    FilePath = saved.FilePath,
                    ExportedAt = now
                });
            }

            var completion = successful.Count > 0
                ? _itemWorkflowService.CompleteExport(successful.Select(item => item.Item), unsuccessfulIds)
                : new BatchCompletionResult(0, 0);

            _cacheMaintenanceService.Compact();
            RefreshItems();

            if (successful.Count > 0 && !string.IsNullOrWhiteSpace(result.ExportDirectory))
            {
                try
                {
                    PathLauncher.OpenDirectory(result.ExportDirectory);
                }
                catch (Exception openException)
                {
                    MessageBox.Show(
                        this,
                        $"种子已保存，但无法自动打开目录：{openException.Message}",
                        title,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }

            MessageBox.Show(
                this,
                BuildTorrentExportMessage(result, completion),
                title,
                MessageBoxButtons.OK,
                successful.Count > 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            return successful.Count;
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "导出种子失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 0;
        }
    }

    private static string BuildTorrentExportMessage(
        TorrentExportResult result,
        BatchCompletionResult completion)
    {
        var message = $"已保存 {result.SuccessCount} 个种子文件。";
        if (!string.IsNullOrWhiteSpace(result.ExportDirectory))
        {
            message += $"{Environment.NewLine}目录：{result.ExportDirectory}";
        }

        if (completion.DiscardedCount > 0)
        {
            message += $"{Environment.NewLine}本批次剩余 {completion.DiscardedCount} 条已转为暂不导出。";
        }

        if (result.SkippedCount > 0)
        {
            message += $"{Environment.NewLine}跳过 {result.SkippedCount} 条：没有 torrent URL。";
        }

        if (result.FailureCount > 0)
        {
            var failedLines = result.Results
                .Where(item => !item.Succeeded && !item.Skipped)
                .Take(8)
                .Select(item => $"• {TrimTitle(item.Item.Title)}：{item.Message}");
            message += $"{Environment.NewLine}失败 {result.FailureCount} 条：{Environment.NewLine}{string.Join(Environment.NewLine, failedLines)}";
            if (result.FailureCount > 8)
            {
                message += $"{Environment.NewLine}……其余失败项保留原状态。";
            }
        }

        if (result.SuccessCount == 0 && result.FailureCount == 0 && result.SkippedCount == 0)
        {
            message = "没有可导出的种子文件。";
        }

        return message;
    }

    private void HandleFilterChanged()
    {
        if (!string.Equals(_filterTextBox.Text, _currentFilterService.CurrentExpression, StringComparison.Ordinal))
        {
            _filterTextBox.Text = _currentFilterService.CurrentExpression;
        }

        _filterErrorLabel.Text = string.Empty;
        _itemFilterService.ReevaluateAll();
        UpdateConditionSummary();
        RefreshItems();
    }

    private void UpdateConditionSummary(string? error = null)
    {
        var expression = _currentFilterService.CurrentExpression;
        _conditionSummaryLabel.Text = string.IsNullOrWhiteSpace(error)
            ? string.IsNullOrWhiteSpace(expression)
                ? "当前条件：无"
                : $"当前条件：{TrimExpression(expression)}"
            : $"当前条件错误：{error}";
        _conditionSummaryLabel.ForeColor = string.IsNullOrWhiteSpace(error)
            ? Color.FromArgb(44, 72, 74)
            : Color.Firebrick;
    }

    private void UpdateBatchSummary()
    {
        var batch = _itemWorkflowService.ActiveBatch;
        _cancelBatchButton.Visible = batch.IsActive;
        _batchSummaryLabel.Text = batch.IsActive
            ? $"待结算批次：复制磁力或导出种子后，未勾选项会转为暂不导出（{batch.ItemIds.Count} 条，{batch.CreatedAt.ToLocalTime():HH:mm:ss}）"
            : "无待结算批次：手动复制只会标记实际导出的条目。";
        _batchSummaryLabel.ForeColor = batch.IsActive
            ? Color.FromArgb(174, 112, 0)
            : UiTheme.AccentDarkColor;
    }

    private static string TrimExpression(string expression)
    {
        return expression.Length <= 80 ? expression : expression[..77] + "...";
    }

    private static string TrimTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return "未命名条目";
        }

        return title.Length <= 48 ? title : title[..45] + "...";
    }

    private void HandleRunCompleted(SchedulerRunResult result)
    {
        _cacheMaintenanceService.Compact();
        RefreshItems();
        if (!result.IsManual)
        {
            return;
        }

        var succeeded = result.Results.Count(item => item.Succeeded);
        var failed = result.Results.Count - succeeded;
        var newCount = result.Results.Sum(item => item.NewMatchedMagnetCount);
        var summary = $"检查完成：成功 {succeeded}，失败 {failed}，新增匹配 magnet {newCount} 条。";
        var notableMessages = result.Results
            .Where(item => !item.Succeeded || item.MagnetCount == 0 || !string.IsNullOrWhiteSpace(item.Warning))
            .Select(item => $"{item.Feed.Name}：{item.Message}{(string.IsNullOrWhiteSpace(item.Warning) ? string.Empty : $"；{item.Warning}")}")
            .ToList();

        if (notableMessages.Count > 0)
        {
            summary += Environment.NewLine + string.Join(Environment.NewLine, notableMessages);
        }

        MessageBox.Show(this, summary, "RSS 检查结果", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ItemGridOnCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (_itemGrid.Rows[e.RowIndex].DataBoundItem is not MagnetItem item)
        {
            return;
        }

        if (string.Equals(item.ProcessingStatus, ProcessingStatuses.Deleted, StringComparison.Ordinal))
        {
            e.CellStyle.ForeColor = Color.FromArgb(142, 144, 140);
            e.CellStyle.BackColor = Color.FromArgb(241, 239, 233);
            e.CellStyle.SelectionForeColor = Color.FromArgb(90, 92, 88);
            e.CellStyle.SelectionBackColor = Color.FromArgb(226, 224, 218);
            if (_itemGrid.Columns[e.ColumnIndex].DataPropertyName == nameof(MagnetItem.IsChecked))
            {
                e.Value = false;
            }
        }

        if (_itemGrid.Columns[e.ColumnIndex].Name == "FeedName")
        {
            e.Value = _feeds.FirstOrDefault(feed => feed.Id == item.FeedId)?.Name ?? item.FeedId;
        }
        else if (_itemGrid.Columns[e.ColumnIndex].Name == "Indicator")
        {
            e.Value = GetIndicatorText(item);
            e.CellStyle.Font = new Font(_itemGrid.Font.FontFamily, 14, FontStyle.Bold);
            e.CellStyle.ForeColor = GetIndicatorColor(item);
            e.CellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        }
        else if (_itemGrid.Columns[e.ColumnIndex].Name == nameof(MagnetItem.IsNew))
        {
            e.Value = item.IsNew && string.Equals(item.ProcessingStatus, ProcessingStatuses.Pending, StringComparison.Ordinal)
                ? "NEW"
                : string.Empty;
        }
        else if (_itemGrid.Columns[e.ColumnIndex].Name == "Status")
        {
            e.Value = string.Equals(item.ProcessingStatus, ProcessingStatuses.Deleted, StringComparison.Ordinal)
                ? "已删除"
                : TranslateStatus(item.MatchStatus);
        }
        else if (_itemGrid.Columns[e.ColumnIndex].Name == "MagnetReady")
        {
            e.Value = string.IsNullOrWhiteSpace(item.Magnet) ? "—" : "可复制";
            e.CellStyle.ForeColor = string.IsNullOrWhiteSpace(item.Magnet)
                ? Color.FromArgb(145, 154, 154)
                : UiTheme.AccentDarkColor;
            e.CellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        }
        else if (_itemGrid.Columns[e.ColumnIndex].Name == "TorrentReady")
        {
            e.Value = string.IsNullOrWhiteSpace(item.TorrentUrl) ? "—" : "可导出";
            e.CellStyle.ForeColor = string.IsNullOrWhiteSpace(item.TorrentUrl)
                ? Color.FromArgb(145, 154, 154)
                : Color.FromArgb(120, 92, 18);
            e.CellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        }
    }

    private void ItemGridOnCellBeginEdit(object? sender, DataGridViewCellCancelEventArgs e)
    {
        if (e.RowIndex < 0 || _itemGrid.Columns[e.ColumnIndex].DataPropertyName != nameof(MagnetItem.IsChecked))
        {
            return;
        }

        if (_itemGrid.Rows[e.RowIndex].DataBoundItem is MagnetItem item
            && string.Equals(item.ProcessingStatus, ProcessingStatuses.Deleted, StringComparison.Ordinal))
        {
            e.Cancel = true;
        }
    }

    private void ItemGridOnCellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || _itemGrid.Columns[e.ColumnIndex].DataPropertyName != nameof(MagnetItem.IsChecked))
        {
            return;
        }

        if (_itemGrid.Rows[e.RowIndex].DataBoundItem is not MagnetItem item)
        {
            return;
        }

        if (!_itemWorkflowService.ApplyManualChecked(item))
        {
            _itemGrid.Refresh();
        }

        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var items = _itemStore.LoadLatest();
        CurrentStatus = _statusService.Calculate(
            _feeds,
            _feedStateStore.Load(),
            items,
            _scheduler.Snapshot);

        var nextCheck = CurrentStatus.NextCheckAt.HasValue
            ? CurrentStatus.NextCheckAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            : "等待首次检查";
        var progress = CurrentStatus.State == ApplicationState.Checking
            ? $" | 进度：{CurrentStatus.CompletedChecks}/{CurrentStatus.TotalChecks}"
            : string.Empty;
        _stateStatusLabel.Text = $"状态：{_statusService.GetDisplayName(CurrentStatus.State)}{progress}";
        _countsStatusLabel.Text =
            $"可见：{_visibleItems.Count} | 缓存：{items.Count} | 待导出：{items.Count(item => string.Equals(item.ProcessingStatus, ProcessingStatuses.Pending, StringComparison.Ordinal) && (!string.IsNullOrWhiteSpace(item.Magnet) || !string.IsNullOrWhiteSpace(item.TorrentUrl)))} | "
            + $"暂不导出：{items.Count(item => string.Equals(item.ProcessingStatus, ProcessingStatuses.Discarded, StringComparison.Ordinal))} | "
            + $"已勾选：{items.Count(item => item.IsChecked)} | 失败：{CurrentStatus.FailedFeedCount}";
        _nextCheckStatusLabel.Text = $"下次检查：{nextCheck}";
        SetActionButtonsEnabled(!_scheduler.Snapshot.IsChecking);
        UpdateConditionSummary();
        UpdateBatchSummary();
        UpdateSearchStatus();
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetActionButtonsEnabled(bool enabled)
    {
        foreach (var item in _interactiveItems)
        {
            item.Enabled = enabled;
        }
    }

    private void RunOnUiThread(Action action)
    {
        if (IsHandleCreated && InvokeRequired)
        {
            BeginInvoke(action);
        }
        else
        {
            action();
        }
    }

    private static string TranslateStatus(string status)
    {
        return status switch
        {
            MatchStatuses.Extracted => "已提取",
            MatchStatuses.NoMagnet => "无 magnet",
            MatchStatuses.TorrentOnly => "torrent only",
            MatchStatuses.Filtered => "被规则过滤",
            MatchStatuses.Exported => "已导出",
            _ => status
        };
    }

    private static string GetIndicatorText(MagnetItem item)
    {
        return (item.ProcessingStatus is ProcessingStatuses.Pending or ProcessingStatuses.Discarded)
            && (!string.IsNullOrWhiteSpace(item.Magnet) || !string.IsNullOrWhiteSpace(item.TorrentUrl))
            ? "●"
            : string.Empty;
    }

    private static Color GetIndicatorColor(MagnetItem item)
    {
        return item.ProcessingStatus switch
        {
            ProcessingStatuses.Pending => Color.FromArgb(30, 168, 96),
            ProcessingStatuses.Discarded => Color.FromArgb(218, 165, 32),
            _ => Color.Transparent
        };
    }

    private sealed record FeedListEntry(ItemListViewMode Mode, string? FeedId, string Name)
    {
        public override string ToString()
        {
            return Name;
        }
    }
}
