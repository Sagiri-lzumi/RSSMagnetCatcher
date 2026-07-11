using RSSMagnetCatcher.Core.Models;
using RSSMagnetCatcher.Core.Services;
using RSSMagnetCatcher.Storage;

namespace RSSMagnetCatcher.UI;

public sealed class DiagnosticsForm : Form
{
    private readonly Func<IReadOnlyList<FeedConfig>> _feedsProvider;
    private readonly FeedStateStore _feedStateStore;
    private readonly FeedDiagnosticsService _diagnosticsService;
    private readonly DataGridView _grid = new();

    public DiagnosticsForm(
        Func<IReadOnlyList<FeedConfig>> feedsProvider,
        FeedStateStore feedStateStore,
        FeedDiagnosticsService diagnosticsService)
    {
        _feedsProvider = feedsProvider;
        _feedStateStore = feedStateStore;
        _diagnosticsService = diagnosticsService;
        Text = "RSS 诊断";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1180, 520);
        MinimumSize = new Size(900, 420);

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AutoGenerateColumns = true;
        _grid.RowHeadersVisible = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(8) };
        buttons.Controls.Add(CreateButton("刷新", Reload));
        buttons.Controls.Add(CreateButton("复制失败诊断", CopyFailures));
        buttons.Controls.Add(CreateButton("关闭", Close));
        Controls.Add(_grid);
        Controls.Add(buttons);
        UiTheme.Apply(this);
        Reload();
    }

    private void Reload()
    {
        var states = _feedStateStore.Load();
        _grid.DataSource = _feedsProvider()
            .Select(feed =>
            {
                var state = states.GetValueOrDefault(feed.Id) ?? new FeedState();
                return new DiagnosticRow
                {
                    订阅 = feed.Name,
                    启用 = feed.Enabled ? "是" : "否",
                    状态 = state.LastStatus,
                    分类 = _diagnosticsService.GetDisplayName(state.LastErrorCategory),
                    HTTP = state.HttpStatusCode?.ToString() ?? "-",
                    XML已解析 = state.ParsedXml ? "是" : "否",
                    条目 = state.LastEntryCount,
                    Magnet = state.LastMagnetCount,
                    匹配 = state.LastMatchedMagnetCount,
                    RSS条目 = state.LastRssEntryCount,
                    历史补抓 = state.LastHistoryBackfillEntryCount,
                    补抓目标 = state.CompletedHistoryBackfillTarget,
                    补抓警告 = state.HistoryBackfillWarning,
                    新增 = state.LastNewCount,
                    连续失败 = state.ConsecutiveFailCount,
                    最后检查 = FormatTime(state.LastCheckedAt),
                    下次检查 = FormatTime(state.NextCheckAt),
                    错误 = state.LastError
                };
            })
            .ToList();
    }

    private void CopyFailures()
    {
        var report = _diagnosticsService.BuildFailureReport(_feedsProvider(), _feedStateStore.Load());
        if (string.IsNullOrWhiteSpace(report))
        {
            MessageBox.Show(this, "当前没有失败订阅。", "复制失败诊断", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Clipboard.SetText(report);
        MessageBox.Show(this, "失败诊断已复制到剪贴板。", "复制完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static string FormatTime(DateTimeOffset? value)
    {
        return value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "-";
    }

    private static Button CreateButton(string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += (_, _) => action();
        return button;
    }

    private sealed class DiagnosticRow
    {
        public string 订阅 { get; init; } = string.Empty;
        public string 启用 { get; init; } = string.Empty;
        public string 状态 { get; init; } = string.Empty;
        public string 分类 { get; init; } = string.Empty;
        public string HTTP { get; init; } = string.Empty;
        public string XML已解析 { get; init; } = string.Empty;
        public int 条目 { get; init; }
        public int Magnet { get; init; }
        public int 匹配 { get; init; }
        public int RSS条目 { get; init; }
        public int 历史补抓 { get; init; }
        public int 补抓目标 { get; init; }
        public string 补抓警告 { get; init; } = string.Empty;
        public int 新增 { get; init; }
        public int 连续失败 { get; init; }
        public string 最后检查 { get; init; } = string.Empty;
        public string 下次检查 { get; init; } = string.Empty;
        public string 错误 { get; init; } = string.Empty;
    }
}
