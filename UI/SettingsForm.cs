using RSSMagnetCatcher.Core.Models;
using RSSMagnetCatcher.Core.Services;
using RSSMagnetCatcher.Infrastructure;
using RSSMagnetCatcher.Storage;

namespace RSSMagnetCatcher.UI;

public sealed class SettingsForm : Form
{
    private readonly DataPaths _paths;
    private readonly JsonConfigStore _configStore;
    private readonly AppSettings _settings;
    private readonly RulePresetService _presetService;
    private readonly RuleMatchService _ruleMatchService;
    private readonly CacheMaintenanceService _cacheMaintenanceService;
    private readonly StartupManager _startupManager;
    private readonly CheckBox _autoStartCheckBox = new();
    private readonly CheckBox _startMinimizedCheckBox = new();
    private readonly CheckBox _checkAllOnStartupCheckBox = new();
    private readonly CheckBox _closeToTrayCheckBox = new();
    private readonly NumericUpDown _globalIntervalInput = CreateNumeric(1, 10080);
    private readonly NumericUpDown _startupDelayInput = CreateNumeric(0, 3600);
    private readonly NumericUpDown _failedRetryInput = CreateNumeric(1, 10080);
    private readonly NumericUpDown _requestIntervalInput = CreateNumeric(1, 3);
    private readonly CheckBox _markExportedCheckBox = new();
    private readonly CheckBox _hideExportedCheckBox = new();
    private readonly ComboBox _defaultRuleComboBox = new();
    private readonly NumericUpDown _maxCacheItemsInput = CreateNumeric(1, 1000000);
    private readonly NumericUpDown _maxArticlesPerFeedInput = CreateNumeric(100, 10000);
    private readonly NumericUpDown _keepHistoryDaysInput = CreateNumeric(0, 36500);

    public SettingsForm(
        DataPaths paths,
        JsonConfigStore configStore,
        AppSettings settings,
        RulePresetService presetService,
        RuleMatchService ruleMatchService,
        CacheMaintenanceService cacheMaintenanceService,
        StartupManager startupManager)
    {
        _paths = paths;
        _configStore = configStore;
        _settings = settings;
        _presetService = presetService;
        _ruleMatchService = ruleMatchService;
        _cacheMaintenanceService = cacheMaintenanceService;
        _startupManager = startupManager;

        Text = "设置";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(700, 500);
        MinimumSize = new Size(620, 460);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildBasicTab());
        tabs.TabPages.Add(BuildCheckTab());
        tabs.TabPages.Add(BuildExportTab());
        tabs.TabPages.Add(BuildRulesTab());
        tabs.TabPages.Add(BuildCacheTab());

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            Padding = new Padding(8),
            FlowDirection = FlowDirection.RightToLeft
        };
        var cancelButton = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
        var saveButton = new Button { Text = "保存", AutoSize = true, DialogResult = DialogResult.OK };
        buttons.Controls.Add(cancelButton);
        buttons.Controls.Add(saveButton);
        Controls.Add(tabs);
        Controls.Add(buttons);
        AcceptButton = saveButton;
        CancelButton = cancelButton;

        LoadValues();
        UiTheme.Apply(this);
    }

    public bool RulesChanged { get; private set; }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK)
        {
            try
            {
                SaveValues();
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "无法保存设置", MessageBoxButtons.OK, MessageBoxIcon.Error);
                e.Cancel = true;
                return;
            }
        }

        base.OnFormClosing(e);
    }

    private TabPage BuildBasicTab()
    {
        var page = CreatePage("基础设置");
        _autoStartCheckBox.Text = "开机自动启动";
        _startMinimizedCheckBox.Text = "启动后进入托盘";
        _checkAllOnStartupCheckBox.Text = "启动后立即检查全部订阅";
        _closeToTrayCheckBox.Text = "关闭窗口时最小化到托盘";
        page.Controls.Add(_autoStartCheckBox);
        page.Controls.Add(_startMinimizedCheckBox);
        page.Controls.Add(_checkAllOnStartupCheckBox);
        page.Controls.Add(_closeToTrayCheckBox);
        return WrapPage("基础设置", page);
    }

    private TabPage BuildCheckTab()
    {
        var page = CreatePage("检查设置");
        page.Controls.Add(CreateNumericRow("全局更新间隔：", _globalIntervalInput, "分钟"));
        page.Controls.Add(CreateNumericRow("启动后延迟检查：", _startupDelayInput, "秒"));
        page.Controls.Add(CreateNumericRow("失败重试间隔：", _failedRetryInput, "分钟"));
        page.Controls.Add(CreateNumericRow("每个 RSS 请求间隔：", _requestIntervalInput, "秒"));
        page.Controls.Add(CreateNumericRow("每个订阅最大保留文章数：", _maxArticlesPerFeedInput, "条"));
        return WrapPage("检查设置", page);
    }

    private TabPage BuildExportTab()
    {
        var page = CreatePage("导出设置");
        page.Controls.Add(new CheckBox { Text = "默认复制到剪贴板（当前版本固定）", Checked = true, Enabled = false, AutoSize = true });
        _markExportedCheckBox.Text = "复制后标记为已使用（当前版本固定）";
        _markExportedCheckBox.Enabled = false;
        _hideExportedCheckBox.Text = "复制后隐藏已导出项";
        page.Controls.Add(_markExportedCheckBox);
        page.Controls.Add(_hideExportedCheckBox);
        page.Controls.Add(new Label { Text = "换行格式：Windows CRLF（当前版本固定）", AutoSize = true });
        return WrapPage("导出设置", page);
    }

    private TabPage BuildRulesTab()
    {
        var page = CreatePage("规则设置");
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        row.Controls.Add(new Label { Text = "默认条件：", AutoSize = true, Anchor = AnchorStyles.Left });
        _defaultRuleComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _defaultRuleComboBox.Width = 280;
        row.Controls.Add(_defaultRuleComboBox);
        page.Controls.Add(row);
        page.Controls.Add(CreateButton("管理条件预设", ManageRules));
        return WrapPage("规则设置", page);
    }

    private TabPage BuildCacheTab()
    {
        var page = CreatePage("缓存设置");
        page.Controls.Add(CreateNumericRow("最大缓存条目：", _maxCacheItemsInput, "条"));
        page.Controls.Add(CreateNumericRow("历史保留天数：", _keepHistoryDaysInput, "天"));
        page.Controls.Add(CreateButton("清理已导出历史", CleanExportedHistory));
        page.Controls.Add(CreateButton("打开 data 文件夹", () => PathLauncher.OpenDirectory(_paths.DataDirectory)));
        return WrapPage("缓存设置", page);
    }

    private void LoadValues()
    {
        _autoStartCheckBox.Checked = _settings.AutoStartWithWindows;
        _startMinimizedCheckBox.Checked = _settings.StartMinimizedToTray;
        _checkAllOnStartupCheckBox.Checked = _settings.CheckAllOnStartup;
        _closeToTrayCheckBox.Checked = _settings.CloseWindowToTray;
        _globalIntervalInput.Value = Math.Clamp(_settings.GlobalIntervalMinutes, 1, 10080);
        _startupDelayInput.Value = Math.Clamp(_settings.StartupCheckDelaySeconds, 0, 3600);
        _failedRetryInput.Value = Math.Clamp(_settings.FailedRetryMinutes, 1, 10080);
        _requestIntervalInput.Value = Math.Clamp(_settings.RssRequestIntervalSeconds, 1, 3);
        _markExportedCheckBox.Checked = true;
        _hideExportedCheckBox.Checked = _settings.HideExportedAfterCopy;
        _maxCacheItemsInput.Value = Math.Clamp(_settings.MaxCacheItems, 1, 1000000);
        _maxArticlesPerFeedInput.Value = Math.Clamp(_settings.MaxArticlesPerFeed, 100, 10000);
        _keepHistoryDaysInput.Value = Math.Clamp(_settings.KeepHistoryDays, 0, 36500);
        ReloadRuleChoices(_settings.DefaultRuleId);
    }

    private void SaveValues()
    {
        _settings.AutoStartWithWindows = _autoStartCheckBox.Checked;
        _settings.StartMinimizedToTray = _startMinimizedCheckBox.Checked;
        _settings.CheckAllOnStartup = _checkAllOnStartupCheckBox.Checked;
        _settings.CloseWindowToTray = _closeToTrayCheckBox.Checked;
        _settings.GlobalIntervalMinutes = (int)_globalIntervalInput.Value;
        _settings.StartupCheckDelaySeconds = (int)_startupDelayInput.Value;
        _settings.FailedRetryMinutes = (int)_failedRetryInput.Value;
        _settings.RssRequestIntervalSeconds = (int)_requestIntervalInput.Value;
        _settings.CopyAfterActionMarkExported = true;
        _settings.HideExportedAfterCopy = _hideExportedCheckBox.Checked;
        _settings.DefaultRuleId = (_defaultRuleComboBox.SelectedItem as RuleChoice)?.Id ?? string.Empty;
        _settings.MaxCacheItems = (int)_maxCacheItemsInput.Value;
        _settings.MaxArticlesPerFeed = (int)_maxArticlesPerFeedInput.Value;
        _settings.KeepHistoryDays = (int)_keepHistoryDaysInput.Value;
        _settings.ClipboardLineEnding = "CRLF";
        _configStore.Save(_paths.SettingsFile, _settings);
        _startupManager.SetEnabled(_settings.AutoStartWithWindows, GetExecutablePath());
        _cacheMaintenanceService.Compact();
    }

    private void ManageRules()
    {
        using var form = new RuleManagerForm(_presetService, _ruleMatchService);
        form.ShowDialog(this);
        if (form.HasChanges)
        {
            RulesChanged = true;
            ReloadRuleChoices(_settings.DefaultRuleId);
        }
    }

    private void CleanExportedHistory()
    {
        if (MessageBox.Show(
            this,
            "将删除已使用缓存和导出历史，未使用与暂时废弃项会保留。是否继续？",
            "清理已导出历史",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        var result = _cacheMaintenanceService.CleanExportedHistory();
        MessageBox.Show(
            this,
            $"已清理 {result.RemovedItems} 条已导出缓存和 {result.RemovedHistoryEntries} 条导出历史。",
            "清理完成",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void ReloadRuleChoices(string? selectedId)
    {
        _defaultRuleComboBox.Items.Clear();
        _defaultRuleComboBox.DisplayMember = nameof(RuleChoice.Name);
        _defaultRuleComboBox.ValueMember = nameof(RuleChoice.Id);
        _defaultRuleComboBox.Items.Add(new RuleChoice(string.Empty, "无"));
        foreach (var rule in _presetService.Load().Where(rule => rule.Enabled))
        {
            _defaultRuleComboBox.Items.Add(new RuleChoice(rule.Id, rule.Name));
        }

        _defaultRuleComboBox.SelectedItem = _defaultRuleComboBox.Items
            .Cast<RuleChoice>()
            .FirstOrDefault(choice => string.Equals(choice.Id, selectedId, StringComparison.Ordinal))
            ?? _defaultRuleComboBox.Items[0];
    }

    private static FlowLayoutPanel CreatePage(string name)
    {
        return new FlowLayoutPanel
        {
            Name = name,
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true
        };
    }

    private static TabPage WrapPage(string title, Control content)
    {
        var page = new TabPage(title);
        page.Controls.Add(content);
        return page;
    }

    private static NumericUpDown CreateNumeric(int minimum, int maximum)
    {
        return new NumericUpDown { Minimum = minimum, Maximum = maximum, Width = 100 };
    }

    private static Control CreateNumericRow(string label, NumericUpDown input, string suffix)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        row.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left });
        row.Controls.Add(input);
        row.Controls.Add(new Label { Text = suffix, AutoSize = true, Anchor = AnchorStyles.Left });
        return row;
    }

    private static Button CreateButton(string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += (_, _) => action();
        return button;
    }

    private static string GetExecutablePath()
    {
        return Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "RRSMC.exe");
    }

    private sealed record RuleChoice(string Id, string Name);
}
