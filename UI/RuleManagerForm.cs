using RSSMagnetCatcher.Core.Models;
using RSSMagnetCatcher.Core.Services;

namespace RSSMagnetCatcher.UI;

public sealed class RuleManagerForm : Form
{
    private readonly RulePresetService _presetService;
    private readonly RuleMatchService _ruleMatchService;
    private readonly ListBox _ruleListBox = new();

    public RuleManagerForm(RulePresetService presetService, RuleMatchService ruleMatchService)
    {
        _presetService = presetService;
        _ruleMatchService = ruleMatchService;
        Text = "管理条件预设";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(620, 360);
        MinimumSize = new Size(520, 300);

        _ruleListBox.Dock = DockStyle.Fill;
        _ruleListBox.DoubleClick += (_, _) => EditSelected();
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(8) };
        buttons.Controls.Add(CreateButton("新增", AddRule));
        buttons.Controls.Add(CreateButton("编辑", EditSelected));
        buttons.Controls.Add(CreateButton("删除", DeleteSelected));
        buttons.Controls.Add(CreateButton("关闭", Close));
        Controls.Add(_ruleListBox);
        Controls.Add(buttons);
        UiTheme.Apply(this);
        Reload();
    }

    public bool HasChanges { get; private set; }

    private void AddRule()
    {
        using var form = new RuleEditForm(_ruleMatchService);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            _presetService.Save(form.Rule);
            HasChanges = true;
            Reload(form.Rule.Id);
        }
    }

    private void EditSelected()
    {
        if (_ruleListBox.SelectedItem is not RuleListEntry selected)
        {
            return;
        }

        using var form = new RuleEditForm(_ruleMatchService, selected.Rule);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            _presetService.Save(form.Rule);
            HasChanges = true;
            Reload(form.Rule.Id);
        }
    }

    private void DeleteSelected()
    {
        if (_ruleListBox.SelectedItem is not RuleListEntry selected
            || MessageBox.Show(
                this,
                $"确定删除条件预设“{selected.Rule.Name}”吗？",
                "删除条件预设",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        _presetService.Delete(selected.Rule.Id);
        HasChanges = true;
        Reload();
    }

    private void Reload(string? selectedId = null)
    {
        _ruleListBox.Items.Clear();
        foreach (var rule in _presetService.Load())
        {
            _ruleListBox.Items.Add(new RuleListEntry(rule));
        }

        if (selectedId is not null)
        {
            _ruleListBox.SelectedItem = _ruleListBox.Items
                .Cast<RuleListEntry>()
                .FirstOrDefault(item => string.Equals(item.Rule.Id, selectedId, StringComparison.Ordinal));
        }
    }

    private static Button CreateButton(string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += (_, _) => action();
        return button;
    }

    private sealed record RuleListEntry(FilterRule Rule)
    {
        public override string ToString()
        {
            var enabled = Rule.Enabled ? "启用" : "停用";
            var quick = Rule.ShowAsQuickButton ? "快捷" : "普通";
            return $"{Rule.Name} [{enabled} / {quick}]";
        }
    }
}
