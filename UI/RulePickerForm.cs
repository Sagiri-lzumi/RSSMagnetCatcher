using RSSMagnetCatcher.Core.Models;
using RSSMagnetCatcher.Core.Services;

namespace RSSMagnetCatcher.UI;

public sealed class RulePickerForm : Form
{
    private readonly RulePresetService _presetService;
    private readonly RuleMatchService _ruleMatchService;
    private readonly RulePickerExpressionService _expressionService;
    private readonly List<(CheckBox CheckBox, string Clause)> _options = [];
    private readonly TextBox _includeTextBox = new();
    private readonly TextBox _excludeTextBox = new();

    public RulePickerForm(
        string currentExpression,
        RulePresetService presetService,
        RuleMatchService ruleMatchService)
    {
        _presetService = presetService;
        _ruleMatchService = ruleMatchService;
        _expressionService = new RulePickerExpressionService(ruleMatchService);
        Text = "条件选择";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(680, 560);
        MinimumSize = new Size(600, 500);

        var content = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true
        };
        content.Controls.Add(CreateGroup("语言", [
            ("GBK / CHS / 简体", "GBK|CHS|简|简体|SC"),
            ("BIG5 / CHT / 繁体", "BIG5|CHT|繁|繁体|TC")
        ]));
        content.Controls.Add(CreateGroup("清晰度", [
            ("720p", "720p"),
            ("1080p", "1080p"),
            ("1080p 及以上", "1080p|1080i|1440p|2160p|4k|uhd"),
            ("2160p / 4K", "2160p|4k|uhd")
        ]));
        content.Controls.Add(CreateGroup("编码", [
            ("HEVC / H265", "HEVC|H265"),
            ("AVC / H264", "AVC|H264")
        ]));
        content.Controls.Add(new Label { Text = "自定义包含正则：", AutoSize = true });
        _includeTextBox.Width = 620;
        _includeTextBox.Text = currentExpression;
        content.Controls.Add(_includeTextBox);
        content.Controls.Add(new Label { Text = "自定义排除正则：", AutoSize = true });
        _excludeTextBox.Width = 620;
        content.Controls.Add(_excludeTextBox);

        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        buttons.Controls.Add(CreateButton("保存为预设", SavePreset));
        var applyButton = new Button { Text = "应用", AutoSize = true, DialogResult = DialogResult.OK };
        buttons.Controls.Add(applyButton);
        buttons.Controls.Add(new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel });
        content.Controls.Add(buttons);
        Controls.Add(content);
        AcceptButton = applyButton;
        CancelButton = buttons.Controls.OfType<Button>().Last();
        UiTheme.Apply(this);
    }

    public string Expression { get; private set; } = string.Empty;

    public bool PresetsChanged { get; private set; }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        string? expression = null;
        if (DialogResult == DialogResult.OK
            && !TryBuildExpression(out expression, out var error))
        {
            MessageBox.Show(this, error, "条件表达式无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            e.Cancel = true;
            return;
        }

        if (DialogResult == DialogResult.OK)
        {
            Expression = expression!;
        }

        base.OnFormClosing(e);
    }

    private GroupBox CreateGroup(string title, IEnumerable<(string Label, string Clause)> options)
    {
        var group = new GroupBox { Text = title, AutoSize = true, Width = 640 };
        var panel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        foreach (var option in options)
        {
            var checkBox = new CheckBox { Text = option.Label, AutoSize = true };
            _options.Add((checkBox, option.Clause));
            panel.Controls.Add(checkBox);
        }

        group.Controls.Add(panel);
        return group;
    }

    private void SavePreset()
    {
        if (!TryBuildExpression(out _, out var error))
        {
            MessageBox.Show(this, error, "条件表达式无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var initialRule = new FilterRule
        {
            IncludeExpression = _expressionService.BuildIncludeExpression(GetSelectedClauses(), _includeTextBox.Text),
            ExcludeExpression = _excludeTextBox.Text.Trim()
        };
        using var form = new RuleEditForm(_ruleMatchService, initialRule);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            form.Rule.Id = string.Empty;
            _presetService.Save(form.Rule);
            PresetsChanged = true;
        }
    }

    private bool TryBuildExpression(out string? expression, out string error)
    {
        var succeeded = _expressionService.TryBuildExpression(
            GetSelectedClauses(),
            _includeTextBox.Text,
            _excludeTextBox.Text,
            out var builtExpression,
            out error);
        expression = succeeded ? builtExpression : null;
        return succeeded;
    }

    private IEnumerable<string> GetSelectedClauses()
    {
        return _options.Where(option => option.CheckBox.Checked).Select(option => option.Clause);
    }

    private static Button CreateButton(string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += (_, _) => action();
        return button;
    }
}
