using RSSMagnetCatcher.Core.Models;
using RSSMagnetCatcher.Core.Services;
using RSSMagnetCatcher.UI;

namespace RSSMagnetCatcher.App;

public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly MainForm _mainForm;
    private readonly FeedScheduler _scheduler;
    private readonly ApplicationStatusService _statusService;
    private readonly AppSettings _settings;
    private readonly RulePresetService _rulePresetService;
    private readonly NotifyIcon _notifyIcon;
    private readonly IDisposable _disposable;
    private readonly Dictionary<ApplicationState, Icon> _icons;
    private readonly ToolStripMenuItem _pauseMenuItem = new();
    private readonly ToolStripMenuItem _statusMenuItem = new();
    private readonly ToolStripMenuItem _conditionsMenuItem = new("条件");

    public TrayApplicationContext(
        MainForm mainForm,
        FeedScheduler scheduler,
        ApplicationStatusService statusService,
        AppSettings settings,
        RulePresetService rulePresetService,
        IDisposable disposable)
    {
        _mainForm = mainForm;
        _scheduler = scheduler;
        _statusService = statusService;
        _settings = settings;
        _rulePresetService = rulePresetService;
        _disposable = disposable;
        _icons = Enum.GetValues<ApplicationState>()
            .ToDictionary(state => state, TrayIconFactory.Create);
        _notifyIcon = new NotifyIcon
        {
            Icon = _icons[ApplicationState.Normal],
            Text = "RSS Magnet Collector",
            Visible = true,
            ContextMenuStrip = BuildTrayMenu()
        };
        _notifyIcon.MouseClick += (_, eventArgs) =>
        {
            if (eventArgs.Button == MouseButtons.Left)
            {
                _mainForm.ShowFromTray(_mainForm.CurrentStatus.NewCount > 0);
            }
        };
        _mainForm.StatusChanged += (_, _) => UpdateTray();
        _mainForm.RulesChanged += (_, _) => RebuildConditionsMenu();
        _mainForm.ExitRequested += (_, _) => ExitApplication();
        _scheduler.RunCompleted += (_, eventArgs) => ShowAutomaticNotification(eventArgs.Result);

        if (!_settings.StartMinimizedToTray)
        {
            _mainForm.Show();
        }

        UpdateTray();
        _scheduler.Start();
        if (_settings.CheckAllOnStartup)
        {
            _ = _scheduler.CheckAllNowAsync(false);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _scheduler.Dispose();
            _disposable.Dispose();
            _mainForm.Dispose();

            foreach (var icon in _icons.Values)
            {
                icon.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    private ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开主界面", null, (_, _) => _mainForm.ShowFromTray(_mainForm.CurrentStatus.NewCount > 0));
        menu.Items.Add("复制全部待导出磁力", null, (_, _) => CopyFromTray(() => _mainForm.CopyAllUnexported(false)));
        menu.Items.Add("复制符合当前条件的待导出项", null, (_, _) => CopyFromTray(() => _mainForm.CopyMatchingUnexported(false)));
        menu.Items.Add("立即检查全部订阅", null, async (_, _) => await _mainForm.CheckAllAsync());
        menu.Items.Add("只检查失败订阅", null, async (_, _) => await _mainForm.CheckFailedAsync());
        _pauseMenuItem.Click += (_, _) => TogglePause();
        menu.Items.Add(_pauseMenuItem);
        menu.Items.Add(new ToolStripSeparator());

        _statusMenuItem.Enabled = false;
        menu.Items.Add(_statusMenuItem);

        RebuildConditionsMenu();
        menu.Items.Add(_conditionsMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("设置", null, (_, _) => _mainForm.OpenSettings());
        menu.Items.Add("打开日志目录", null, (_, _) => _mainForm.OpenLogsDirectory());
        menu.Items.Add("退出", null, (_, _) => ExitApplication());
        return menu;
    }

    private void RebuildConditionsMenu()
    {
        _conditionsMenuItem.DropDownItems.Clear();
        foreach (var option in FilterQuickOption.All)
        {
            _conditionsMenuItem.DropDownItems.Add(option.Label, null, (_, _) => _mainForm.AppendQuickFilter(option));
        }

        var presetRules = _rulePresetService.Load()
            .Where(rule => rule.Enabled && rule.ShowAsQuickButton)
            .ToList();
        if (presetRules.Count > 0)
        {
            _conditionsMenuItem.DropDownItems.Add(new ToolStripSeparator());
            foreach (var rule in presetRules)
            {
                _conditionsMenuItem.DropDownItems.Add($"预设：{rule.Name}", null, (_, _) => _mainForm.ApplyPreset(rule));
            }
        }

        _conditionsMenuItem.DropDownItems.Add(new ToolStripSeparator());
        _conditionsMenuItem.DropDownItems.Add("打开条件选择", null, (_, _) => _mainForm.OpenRulePicker());
        _conditionsMenuItem.DropDownItems.Add("清空当前条件", null, (_, _) => _mainForm.ClearFilter());
    }

    private void TogglePause()
    {
        if (_scheduler.Snapshot.IsPaused)
        {
            _scheduler.Resume();
        }
        else
        {
            _scheduler.Pause();
        }
    }

    private void CopyFromTray(Func<int> copyAction)
    {
        var count = copyAction();
        _notifyIcon.ShowBalloonTip(
            3000,
            count > 0 ? "复制完成" : "没有可复制项",
            count > 0 ? $"已复制 {count} 条 magnet 到剪贴板。" : "没有符合条件的待导出 magnet。",
            count > 0 ? ToolTipIcon.Info : ToolTipIcon.Warning);
    }

    private void ShowAutomaticNotification(SchedulerRunResult result)
    {
        if (result.IsManual || !result.Started)
        {
            return;
        }

        RunOnUiThread(() =>
        {
            var newCount = result.Results.Sum(item => item.NewMatchedMagnetCount);
            var failedCount = result.Results.Count(item => !item.Succeeded);
            if (newCount == 0 && failedCount == 0)
            {
                return;
            }

            var message = newCount > 0
                ? $"发现 {newCount} 条符合当前条件的新 magnet。"
                : string.Empty;
            if (failedCount > 0)
            {
                message = string.IsNullOrWhiteSpace(message)
                    ? $"{failedCount} 个 RSS 订阅检查失败。"
                    : $"{message}{Environment.NewLine}{failedCount} 个 RSS 订阅检查失败。";
            }

            _notifyIcon.ShowBalloonTip(
                5000,
                failedCount > 0 ? "RSS 检查完成（有警告）" : "发现新 magnet",
                message,
                failedCount > 0 ? ToolTipIcon.Warning : ToolTipIcon.Info);
        });
    }

    private void ExitApplication()
    {
        _mainForm.AllowApplicationClose();
        _notifyIcon.Visible = false;
        _mainForm.Close();
        ExitThread();
    }

    private void UpdateTray()
    {
        RunOnUiThread(() =>
        {
            var status = _mainForm.CurrentStatus;
            _notifyIcon.Icon = _icons[status.State];
            _notifyIcon.Text = TruncateTooltip(BuildTooltip(status));
            _pauseMenuItem.Text = _scheduler.Snapshot.IsPaused
                ? "继续自动检查"
                : "暂停自动检查";
            _statusMenuItem.Text = BuildStatusSummary(status);
        });
    }

    private string BuildTooltip(ApplicationStatusSnapshot status)
    {
        var nextCheck = status.NextCheckAt.HasValue
            ? status.NextCheckAt.Value.ToLocalTime().ToString("HH:mm")
            : "待定";
        return $"{_statusService.GetDisplayName(status.State)}|RSS:{status.FeedCount}|"
            + $"新:{status.NewCount}|败:{status.FailedFeedCount}|下:{nextCheck}";
    }

    private string BuildStatusSummary(ApplicationStatusSnapshot status)
    {
        var nextCheck = status.NextCheckAt.HasValue
            ? status.NextCheckAt.Value.ToLocalTime().ToString("HH:mm:ss")
            : "等待首次检查";
        return $"状态：{_statusService.GetDisplayName(status.State)} | 新增：{status.NewCount} | "
            + $"失败：{status.FailedFeedCount} | 下次：{nextCheck}";
    }

    private void RunOnUiThread(Action action)
    {
        if (_mainForm.IsHandleCreated && _mainForm.InvokeRequired)
        {
            _mainForm.BeginInvoke(action);
        }
        else
        {
            action();
        }
    }

    private static string TruncateTooltip(string value)
    {
        return value.Length <= 63 ? value : value[..63];
    }
}
