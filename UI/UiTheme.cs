using System.Drawing;
using RSSMagnetCatcher.App;
using RSSMagnetCatcher.Core.Models;

namespace RSSMagnetCatcher.UI;

public static class UiTheme
{
    private static Icon? _appIcon;

    private static Icon AppIcon
    {
        get
        {
            if (_appIcon is null)
            {
                _appIcon = TrayIconFactory.Create(ApplicationState.Normal);
            }
            return _appIcon;
        }
    }

    // Window/chrome: cool light gray, not the old warm cream.
    public static Color WindowBackColor { get; } = Color.FromArgb(246, 247, 249);

    // Panels/cards: near-white with a faint cool tint.
    public static Color PanelBackColor { get; } = Color.FromArgb(252, 253, 254);

    // Sidebar gets its own subtly darker surface so it reads as a rail, not a twin of the main panel.
    public static Color SidebarBackColor { get; } = Color.FromArgb(241, 243, 246);

    // Single confident accent: indigo-blue. Used for primary actions and highlights.
    public static Color AccentColor { get; } = Color.FromArgb(37, 99, 235);

    public static Color AccentDarkColor { get; } = Color.FromArgb(29, 78, 216);

    // Soft tint of the accent, used for hover/selection washes.
    public static Color AccentSoftColor { get; } = Color.FromArgb(219, 234, 254);

    public static Color SelectedRowColor { get; } = Color.FromArgb(224, 231, 255);

    public static Color AlternateRowColor { get; } = Color.FromArgb(250, 250, 252);

    // Hairlines and borders: slate-200, crisp without being heavy.
    public static Color HairlineColor { get; } = Color.FromArgb(226, 232, 240);

    // Grid header band, matches sidebar for visual rhythm.
    public static Color GridHeaderColor { get; } = Color.FromArgb(241, 243, 246);

    // Primary text ink: slate-800.
    public static Color InkColor { get; } = Color.FromArgb(30, 41, 59);

    // Secondary/muted text: slate-500.
    public static Color MutedColor { get; } = Color.FromArgb(100, 116, 139);

    // Status colors.
    public static Color DangerColor { get; } = Color.FromArgb(220, 38, 38);
    public static Color SuccessColor { get; } = Color.FromArgb(22, 163, 74);
    public static Color WarningColor { get; } = Color.FromArgb(217, 119, 6);

    public static void Apply(Form form)
    {
        if (form.Icon is null)
        {
            form.Icon = AppIcon;
        }
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
       button.Padding = new Padding(14, 6, 14, 6);
       button.Cursor = Cursors.Hand;
        button.FlatAppearance.MouseOverBackColor = AccentDarkColor;
   }

   public static void StyleSecondaryButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = HairlineColor;
        button.BackColor = PanelBackColor;
        button.ForeColor = InkColor;
        button.Font = new Font(button.Font.FontFamily, button.Font.Size, FontStyle.Regular);
       button.Padding = new Padding(12, 5, 12, 5);
       button.Cursor = Cursors.Hand;
        button.FlatAppearance.MouseOverBackColor = AccentSoftColor;
   }

   public static void StyleToolStrip(ToolStrip toolStrip)
   {
       toolStrip.BackColor = PanelBackColor;
       toolStrip.GripStyle = ToolStripGripStyle.Hidden;
       toolStrip.Padding = new Padding(8, 4, 8, 4);
        toolStrip.Renderer = new FlatToolStripRenderer();
       toolStrip.ForeColor = InkColor;
   }
   public static void StyleContextMenu(ContextMenuStrip menu)
   {
       menu.BackColor = PanelBackColor;
       menu.ForeColor = InkColor;
       menu.Font = new Font("Microsoft YaHei UI", 9F);
       menu.Padding = new Padding(4, 2, 4, 2);
       menu.RenderMode = ToolStripRenderMode.ManagerRenderMode;
       menu.Renderer = new FlatToolStripRenderer();
       menu.ShowImageMargin = false;
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

/// <summary>
/// Flat, theme-aware renderer for MenuStrip/StatusStrip: no system chrome,
/// cool hairline separators, indigo-accent selection.
/// </summary>
internal sealed class FlatToolStripRenderer : ToolStripProfessionalRenderer
{
    public FlatToolStripRenderer()
        : base(new FlatColorTable())
    {
        RoundedEdges = false;
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var rect = new Rectangle(Point.Empty, e.Item.Size);
        var color = e.Item.Selected ? UiTheme.AccentSoftColor : UiTheme.PanelBackColor;
        using var brush = new SolidBrush(color);
        e.Graphics.FillRectangle(brush, rect);
    }

   protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
   {
        var r = e.Item.Bounds;
        var y = r.Y + r.Height / 2 - 1;
       using var pen = new Pen(UiTheme.HairlineColor, 1);
        e.Graphics.DrawLine(pen, r.Left + 8, y, r.Right - 8, y);
   }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        // No outer border — bars blend into the panel surface.
    }

    protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
    {
        var rect = new Rectangle(Point.Empty, e.Item.Size);
        var color = e.Item.Selected ? UiTheme.AccentSoftColor : UiTheme.PanelBackColor;
        using var brush = new SolidBrush(color);
        e.Graphics.FillRectangle(brush, rect);
    }
}

/// <summary>
/// Minimal color table backing <see cref="FlatToolStripRenderer"/>; most overrides
/// just map to the UiTheme palette so the renderer reads as part of the same system.
/// </summary>
internal sealed class FlatColorTable : ProfessionalColorTable
{
    public override Color MenuBorder => UiTheme.HairlineColor;

    public override Color MenuItemBorder => UiTheme.HairlineColor;

    public override Color MenuItemSelected => UiTheme.AccentSoftColor;

    public override Color MenuItemPressedGradientBegin => UiTheme.AccentSoftColor;

    public override Color MenuItemPressedGradientEnd => UiTheme.AccentSoftColor;

    public override Color MenuItemSelectedGradientBegin => UiTheme.AccentSoftColor;

    public override Color MenuItemSelectedGradientEnd => UiTheme.AccentSoftColor;

    public override Color MenuStripGradientBegin => UiTheme.PanelBackColor;

    public override Color MenuStripGradientEnd => UiTheme.PanelBackColor;

    public override Color ToolStripGradientBegin => UiTheme.PanelBackColor;

    public override Color ToolStripGradientMiddle => UiTheme.PanelBackColor;

    public override Color ToolStripGradientEnd => UiTheme.PanelBackColor;

    public override Color StatusStripGradientBegin => UiTheme.PanelBackColor;

    public override Color StatusStripGradientEnd => UiTheme.PanelBackColor;

    public override Color SeparatorDark => UiTheme.HairlineColor;

    public override Color SeparatorLight => UiTheme.HairlineColor;
}
