using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using RSSMagnetCatcher.Core.Models;

namespace RSSMagnetCatcher.App;

public static class TrayIconFactory
{
    public static Icon Create(ApplicationState state)
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        var paused = state == ApplicationState.Paused;
        DrawIcon(graphics, 32, GetColor(state), paused);
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

    public static void DrawIcon(Graphics graphics, int size, Color accentColor, bool paused)
    {
        var padding = size / 16f;
        using var accent = new SolidBrush(accentColor);
        using var accentDark = new SolidBrush(Color.FromArgb(
            Math.Max(0, accentColor.R - 40),
            Math.Max(0, accentColor.G - 40),
            Math.Max(0, accentColor.B - 40)));
        using var outline = new Pen(Color.FromArgb(48, 48, 48), Math.Max(1f, size / 32f));
        using var hairline = new Pen(Color.FromArgb(140, 255, 255, 255), Math.Max(0.75f, size / 42f));

        RectangleF Body(float p) => new(padding + p, padding + p, size - 2 * (padding + p), size - 2 * (padding + p));
        var body = Body(0);
        var radius = size / 5f;
        var path = RoundedPath(body, radius);
        graphics.FillPath(accent, path);
        graphics.DrawPath(outline, path);
        path.Dispose();

        var inner = Body(Math.Max(2f, size / 12f));
        var innerPath = RoundedPath(inner, radius * 0.7f);
        using var innerShade = new SolidBrush(Color.FromArgb(28, 0, 0, 0));
        graphics.FillPath(innerShade, innerPath);
        graphics.DrawPath(hairline, innerPath);
        innerPath.Dispose();

        var cx = body.X + body.Width / 2f;
        var cy = body.Y + body.Height / 2f;
        var feedY = cy + size / 9f;

        var dotR = Math.Max(1.5f, size / 11f);
        var dotRect = new RectangleF(cx - dotR, feedY - dotR, dotR * 2, dotR * 2);
        using var dotPath = RoundedPath(dotRect, dotR);
        using var dotBrush = new SolidBrush(Color.FromArgb(245, 245, 245));
        graphics.FillPath(dotBrush, dotPath);

        float[] radii = { size / 3.4f, size / 2.2f, size / 1.55f };
        for (var i = 0; i < radii.Length; i++)
        {
            var r = radii[i];
            var arcRect = new RectangleF(cx - r, feedY - r, r * 2, r * 2);
            using var arcPath = new GraphicsPath();
            arcPath.AddArc(arcRect, 225, -135);
            using var pen = new Pen(Color.FromArgb(200 - i * 35, 252, 252, 252), Math.Max(1.2f, size / 24f))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            graphics.DrawPath(pen, arcPath);
        }

        var magnetW = body.Width / 2.6f;
        var magnetH = body.Height / 3f;
        var magnetRect = new RectangleF(cx - magnetW / 2f, padding + size / 9f, magnetW, magnetH);
        DrawUMagnet(graphics, magnetRect, accentDark, outline, size);
    }

    private static void DrawUMagnet(Graphics graphics, RectangleF rect, Brush fill, Pen outline, int size)
    {
        var t = Math.Max(1.5f, size / 16f);
        var r = rect.Width / 2f - t / 2f;
        var cy = rect.Bottom - r;
        using var path = new GraphicsPath();
        path.AddArc(rect.X, cy - r, rect.Width, r * 2, 180, 180);
        path.AddRectangle(new RectangleF(rect.X, rect.Y, t, cy - rect.Y));
        path.AddRectangle(new RectangleF(rect.Right - t, rect.Y, t, cy - rect.Y));
        graphics.FillPath(fill, path);
        graphics.DrawPath(outline, path);
    }

    private static GraphicsPath RoundedPath(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return path;
        }

        var d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        radius = d / 2f;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Color GetColor(ApplicationState state)
    {
        return state switch
        {
            ApplicationState.Checking => Color.FromArgb(41, 128, 185),
            ApplicationState.HasNew => Color.FromArgb(39, 174, 96),
            ApplicationState.PartialFailure => Color.FromArgb(211, 137, 36),
            ApplicationState.Offline => Color.FromArgb(192, 57, 43),
            ApplicationState.Paused => Color.FromArgb(149, 165, 166),
            _ => Color.FromArgb(22, 132, 125)
        };
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);
}