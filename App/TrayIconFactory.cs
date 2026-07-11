using System.Runtime.InteropServices;
using RSSMagnetCatcher.Core.Models;

namespace RSSMagnetCatcher.App;

public static class TrayIconFactory
{
    public static Icon Create(ApplicationState state)
    {
        using var bitmap = new Bitmap(16, 16);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using var fill = new SolidBrush(GetColor(state));
        using var outline = new Pen(Color.FromArgb(80, 80, 80), 1);
        graphics.FillEllipse(fill, 2, 2, 12, 12);
        graphics.DrawEllipse(outline, 2, 2, 12, 12);

        var handle = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static Color GetColor(ApplicationState state)
    {
        return state switch
        {
            ApplicationState.Checking => Color.DodgerBlue,
            ApplicationState.HasNew => Color.LimeGreen,
            ApplicationState.PartialFailure => Color.DarkOrange,
            ApplicationState.Offline => Color.Firebrick,
            ApplicationState.Paused => Color.Gray,
            _ => Color.SeaGreen
        };
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);
}
