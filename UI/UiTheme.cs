namespace RSSMagnetCatcher.UI;

public static class UiTheme
{
    public static Color WindowBackColor { get; } = Color.FromArgb(242, 239, 232);

    public static Color PanelBackColor { get; } = Color.FromArgb(255, 253, 248);

    public static Color AccentColor { get; } = Color.FromArgb(22, 132, 125);

    public static Color AccentDarkColor { get; } = Color.FromArgb(15, 92, 88);

    public static Color SelectedRowColor { get; } = Color.FromArgb(218, 240, 236);

    public static Color AlternateRowColor { get; } = Color.FromArgb(250, 247, 240);

    public static Color HairlineColor { get; } = Color.FromArgb(220, 212, 196);

    public static Color InkColor { get; } = Color.FromArgb(36, 49, 48);

    public static void Apply(Form form)
    {
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.BackColor = WindowBackColor;
        form.Font = new Font("Microsoft YaHei UI", 9F);
        ApplyControls(form.Controls);
    }

    public static void StylePrimaryButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = AccentColor;
        button.ForeColor = Color.White;
        button.Font = new Font(button.Font, FontStyle.Bold);
        button.Padding = new Padding(12, 5, 12, 5);
        button.Cursor = Cursors.Hand;
    }

    public static void StyleSecondaryButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = HairlineColor;
        button.BackColor = PanelBackColor;
        button.ForeColor = InkColor;
        button.Padding = new Padding(10, 4, 10, 4);
        button.Cursor = Cursors.Hand;
    }

    public static void StyleToolStrip(ToolStrip toolStrip)
    {
        toolStrip.BackColor = PanelBackColor;
        toolStrip.GripStyle = ToolStripGripStyle.Hidden;
        toolStrip.Padding = new Padding(8, 4, 8, 4);
        toolStrip.RenderMode = ToolStripRenderMode.System;
        toolStrip.ForeColor = InkColor;
    }

    private static void ApplyControls(Control.ControlCollection controls)
    {
        foreach (Control control in controls)
        {
            switch (control)
            {
                case Button button:
                    if (button.Tag is string tag && string.Equals(tag, "primary", StringComparison.Ordinal))
                    {
                        StylePrimaryButton(button);
                    }
                    else
                    {
                        StyleSecondaryButton(button);
                    }
                    break;
                case TextBox:
                case ComboBox:
                case NumericUpDown:
                case ListBox:
                case DataGridView:
                    control.BackColor = Color.White;
                    break;
                case TableLayoutPanel:
                case FlowLayoutPanel:
                case GroupBox:
                case TabPage:
                case Panel:
                    control.BackColor = PanelBackColor;
                    break;
                case ToolStrip toolStrip:
                    StyleToolStrip(toolStrip);
                    break;
            }

            if (control.HasChildren)
            {
                ApplyControls(control.Controls);
            }
        }
    }
}
