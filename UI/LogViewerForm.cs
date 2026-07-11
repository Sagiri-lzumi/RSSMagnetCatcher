using RSSMagnetCatcher.Infrastructure;
using RSSMagnetCatcher.Storage;

namespace RSSMagnetCatcher.UI;

public sealed class LogViewerForm : Form
{
    private readonly DataPaths _paths;
    private readonly ComboBox _logChoice = new();
    private readonly TextBox _contentTextBox = new();

    public LogViewerForm(DataPaths paths)
    {
        _paths = paths;
        Text = "日志查看";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(920, 560);
        MinimumSize = new Size(700, 420);

        var topBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8) };
        _logChoice.DropDownStyle = ComboBoxStyle.DropDownList;
        _logChoice.Items.AddRange(["app.log", "error.log"]);
        _logChoice.SelectedIndexChanged += (_, _) => Reload();
        topBar.Controls.Add(_logChoice);
        topBar.Controls.Add(CreateButton("刷新", Reload));
        topBar.Controls.Add(CreateButton("打开日志目录", () => PathLauncher.OpenDirectory(_paths.LogsDirectory)));

        _contentTextBox.Dock = DockStyle.Fill;
        _contentTextBox.Multiline = true;
        _contentTextBox.ReadOnly = true;
        _contentTextBox.ScrollBars = ScrollBars.Both;
        _contentTextBox.WordWrap = false;
        _contentTextBox.Font = new Font(FontFamily.GenericMonospace, 9);

        Controls.Add(_contentTextBox);
        Controls.Add(topBar);
        UiTheme.Apply(this);
        _logChoice.SelectedIndex = 0;
    }

    private void Reload()
    {
        var path = _logChoice.SelectedIndex == 1 ? _paths.ErrorLogFile : _paths.AppLogFile;
        _contentTextBox.Text = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        _contentTextBox.SelectionStart = _contentTextBox.TextLength;
        _contentTextBox.ScrollToCaret();
    }

    private static Button CreateButton(string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += (_, _) => action();
        return button;
    }
}
