using RSSMagnetCatcher.Core.Models;
using RSSMagnetCatcher.Core.Services;

namespace RSSMagnetCatcher.UI;

public sealed class FeedEditForm : Form
{
    private readonly FeedConfig? _existingFeed;
    private readonly TextBox _nameTextBox = new();
    private readonly TextBox _urlTextBox = new();
    private readonly TextBox _groupTextBox = new();
    private readonly CheckBox _enabledCheckBox = new();
    private readonly CheckBox _useGlobalIntervalCheckBox = new();
    private readonly NumericUpDown _intervalInput = new();
    private readonly ComboBox _defaultRuleComboBox = new();
    private readonly CheckBox _autoCheckNewMatchedItemsCheckBox = new();
    private readonly CheckBox _enableMikanHistoryBackfillCheckBox = new();

    public FeedEditForm(IEnumerable<FilterRule> rules, FeedConfig? existingFeed = null)
    {
        _existingFeed = existingFeed;
        Text = existingFeed is null ? "添加 RSS 订阅" : "编辑 RSS 订阅";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(640, 420);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 2,
            RowCount = 9
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _urlTextBox.Dock = DockStyle.Fill;
        _nameTextBox.Dock = DockStyle.Fill;
        _groupTextBox.Dock = DockStyle.Fill;
        _groupTextBox.Text = existingFeed?.Group ?? "默认";
        _enabledCheckBox.Text = "启用订阅";
        _enabledCheckBox.Checked = existingFeed?.Enabled ?? true;
        _useGlobalIntervalCheckBox.Text = "使用全局间隔";
        _useGlobalIntervalCheckBox.Checked = existingFeed?.UseGlobalInterval ?? true;
        _useGlobalIntervalCheckBox.CheckedChanged += (_, _) => UpdateIntervalEnabled();
        _intervalInput.Minimum = 1;
        _intervalInput.Maximum = 10080;
        _intervalInput.Value = Math.Clamp(existingFeed?.IntervalMinutes ?? 30, 1, 10080);
        _defaultRuleComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _defaultRuleComboBox.DisplayMember = nameof(RuleChoice.Name);
        _defaultRuleComboBox.ValueMember = nameof(RuleChoice.Id);
        _defaultRuleComboBox.Items.Add(new RuleChoice(string.Empty, "无"));
        foreach (var rule in rules.Where(rule => rule.Enabled))
        {
            _defaultRuleComboBox.Items.Add(new RuleChoice(rule.Id, rule.Name));
        }

        SelectRule(existingFeed?.DefaultRuleId);
        _autoCheckNewMatchedItemsCheckBox.Text = "符合当前条件的新条目自动勾选";
        _autoCheckNewMatchedItemsCheckBox.Checked = existingFeed?.AutoCheckNewMatchedItems ?? true;
        _enableMikanHistoryBackfillCheckBox.Text = "启用 Mikan Classic 历史补抓（仅支持公开 Classic 地址）";
        _enableMikanHistoryBackfillCheckBox.Checked = existingFeed?.EnableMikanHistoryBackfill
            ?? MikanHistoryService.IsSupportedUrl(existingFeed?.Url);
        _urlTextBox.TextChanged += (_, _) => UpdateMikanHistoryEnabled();

        _urlTextBox.Text = existingFeed?.Url ?? string.Empty;
        _nameTextBox.Text = existingFeed?.Name ?? string.Empty;

        AddRow(layout, 0, "订阅地址：", _urlTextBox);
        AddRow(layout, 1, "名称：", _nameTextBox);
        AddRow(layout, 2, "分组：", _groupTextBox);
        AddRow(layout, 3, "状态：", _enabledCheckBox);
        AddRow(layout, 4, "检查间隔：", BuildIntervalPanel());
        AddRow(layout, 5, "默认规则：", _defaultRuleComboBox);
        AddRow(layout, 6, "新条目：", _autoCheckNewMatchedItemsCheckBox);
        AddRow(layout, 7, "历史补抓：", _enableMikanHistoryBackfillCheckBox);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill
        };
        var cancelButton = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
        var saveButton = new Button
        {
            Text = existingFeed is null ? "添加" : "保存",
            DialogResult = DialogResult.OK,
            AutoSize = true
        };
        buttons.Controls.Add(cancelButton);
        buttons.Controls.Add(saveButton);
        layout.Controls.Add(buttons, 0, 8);
        layout.SetColumnSpan(buttons, 2);

        Controls.Add(layout);
        AcceptButton = saveButton;
        CancelButton = cancelButton;
        UpdateIntervalEnabled();
        UpdateMikanHistoryEnabled();
        UiTheme.Apply(this);
    }

    public FeedConfig Feed { get; private set; } = new();

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        FeedConfig? feed = null;
        if (DialogResult == DialogResult.OK && !TryCreateFeed(out feed, out var error))
        {
            MessageBox.Show(this, error, "无法保存订阅", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            e.Cancel = true;
            return;
        }

        if (DialogResult == DialogResult.OK)
        {
            Feed = feed!;
        }

        base.OnFormClosing(e);
    }

    private Control BuildIntervalPanel()
    {
        var panel = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        panel.Controls.Add(_useGlobalIntervalCheckBox);
        panel.Controls.Add(new Label { Text = "自定义：", AutoSize = true, Anchor = AnchorStyles.Left });
        panel.Controls.Add(_intervalInput);
        panel.Controls.Add(new Label { Text = "分钟", AutoSize = true, Anchor = AnchorStyles.Left });
        return panel;
    }

    private bool TryCreateFeed(out FeedConfig? feed, out string error)
    {
        feed = null;
        error = string.Empty;
        if (!Uri.TryCreate(_urlTextBox.Text.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            error = "请输入有效的 HTTP 或 HTTPS RSS 地址。";
            return false;
        }

        var name = _nameTextBox.Text.Trim();
        feed = new FeedConfig
        {
            Id = _existingFeed?.Id ?? string.Empty,
            Name = string.IsNullOrWhiteSpace(name) ? uri.Host : name,
            Url = uri.AbsoluteUri,
            Enabled = _enabledCheckBox.Checked,
            Group = string.IsNullOrWhiteSpace(_groupTextBox.Text) ? "默认" : _groupTextBox.Text.Trim(),
            UseGlobalInterval = _useGlobalIntervalCheckBox.Checked,
            IntervalMinutes = (int)_intervalInput.Value,
            DefaultRuleId = (_defaultRuleComboBox.SelectedItem as RuleChoice)?.Id ?? string.Empty,
            AutoCheckNewMatchedItems = _autoCheckNewMatchedItemsCheckBox.Checked,
            EnableMikanHistoryBackfill = _enableMikanHistoryBackfillCheckBox.Checked
        };
        return true;
    }

    private void SelectRule(string? ruleId)
    {
        var index = _defaultRuleComboBox.Items
            .Cast<RuleChoice>()
            .Select((choice, choiceIndex) => (choice, choiceIndex))
            .FirstOrDefault(pair => string.Equals(pair.choice.Id, ruleId, StringComparison.Ordinal))
            .choiceIndex;
        _defaultRuleComboBox.SelectedIndex = index;
    }

    private void UpdateIntervalEnabled()
    {
        _intervalInput.Enabled = !_useGlobalIntervalCheckBox.Checked;
    }

    private void UpdateMikanHistoryEnabled()
    {
        var supported = MikanHistoryService.IsSupportedUrl(_urlTextBox.Text.Trim());
        _enableMikanHistoryBackfillCheckBox.Enabled = supported;
        if (!supported)
        {
            _enableMikanHistoryBackfillCheckBox.Checked = false;
        }
    }

    private static void AddRow(TableLayoutPanel layout, int row, string label, Control control)
    {
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        layout.Controls.Add(control, 1, row);
    }

    private sealed record RuleChoice(string Id, string Name);
}
