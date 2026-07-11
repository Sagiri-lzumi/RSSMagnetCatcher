using RSSMagnetCatcher.Core.Models;
using RSSMagnetCatcher.Core.Services;

namespace RSSMagnetCatcher.UI;

public sealed class RuleEditForm : Form
{
    private readonly RuleMatchService _ruleMatchService;
    private readonly FilterRule? _existingRule;
    private readonly TextBox _nameTextBox = new();
    private readonly TextBox _includeTextBox = new();
    private readonly TextBox _excludeTextBox = new();
    private readonly CheckBox _enabledCheckBox = new();
    private readonly CheckBox _quickCheckBox = new();

    public RuleEditForm(RuleMatchService ruleMatchService, FilterRule? existingRule = null)
    {
        _ruleMatchService = ruleMatchService;
        _existingRule = existingRule;
        Text = existingRule is null ? "新增条件预设" : "编辑条件预设";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(620, 280);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 2,
            RowCount = 6
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _nameTextBox.Text = existingRule?.Name ?? string.Empty;
        _includeTextBox.Text = existingRule?.IncludeExpression ?? string.Empty;
        _excludeTextBox.Text = existingRule?.ExcludeExpression ?? string.Empty;
        _enabledCheckBox.Text = "启用";
        _enabledCheckBox.Checked = existingRule?.Enabled ?? true;
        _quickCheckBox.Text = "显示为快捷入口";
        _quickCheckBox.Checked = existingRule?.ShowAsQuickButton ?? true;
        foreach (var textBox in new[] { _nameTextBox, _includeTextBox, _excludeTextBox })
        {
            textBox.Dock = DockStyle.Fill;
        }

        AddRow(layout, 0, "名称：", _nameTextBox);
        AddRow(layout, 1, "包含表达式：", _includeTextBox);
        AddRow(layout, 2, "排除表达式：", _excludeTextBox);
        AddRow(layout, 3, "状态：", _enabledCheckBox);
        AddRow(layout, 4, "快捷入口：", _quickCheckBox);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill
        };
        var cancelButton = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
        var saveButton = new Button { Text = "保存", DialogResult = DialogResult.OK, AutoSize = true };
        buttons.Controls.Add(cancelButton);
        buttons.Controls.Add(saveButton);
        layout.Controls.Add(buttons, 0, 5);
        layout.SetColumnSpan(buttons, 2);
        Controls.Add(layout);
        AcceptButton = saveButton;
        CancelButton = cancelButton;
        UiTheme.Apply(this);
    }

    public FilterRule Rule { get; private set; } = new();

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        FilterRule? rule = null;
        if (DialogResult == DialogResult.OK && !TryCreateRule(out rule, out var error))
        {
            MessageBox.Show(this, error, "无法保存条件预设", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            e.Cancel = true;
            return;
        }

        if (DialogResult == DialogResult.OK)
        {
            Rule = rule!;
        }

        base.OnFormClosing(e);
    }

    private bool TryCreateRule(out FilterRule? rule, out string error)
    {
        rule = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(_nameTextBox.Text))
        {
            error = "规则名称不能为空。";
            return false;
        }

        rule = new FilterRule
        {
            Id = _existingRule?.Id ?? string.Empty,
            Name = _nameTextBox.Text.Trim(),
            IncludeExpression = _includeTextBox.Text.Trim(),
            ExcludeExpression = _excludeTextBox.Text.Trim(),
            Enabled = _enabledCheckBox.Checked,
            ShowAsQuickButton = _quickCheckBox.Checked
        };
        return _ruleMatchService.TryValidate(_ruleMatchService.BuildExpression(rule), out error);
    }

    private static void AddRow(TableLayoutPanel layout, int row, string label, Control control)
    {
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        layout.Controls.Add(control, 1, row);
    }
}
