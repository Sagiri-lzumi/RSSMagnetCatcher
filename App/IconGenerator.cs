using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace RSSMagnetCatcher.App;

public static class IconGenerator
{
    public static void GenerateTo(string outDir)
    {
        Directory.CreateDirectory(outDir);
        var sizes = new[] { 16, 24, 32, 48, 64, 128, 256 };
        var frames = new List<(int size, byte[] dib)>();
        foreach (var size in sizes)
        {
            using var bitmap = new Bitmap(size, size);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            TrayIconFactory.DrawIcon(graphics, size, Color.FromArgb(22, 132, 125), false);
            frames.Add((size, BitmapToDib(bitmap)));
        }

        var icoPath = Path.Combine(outDir, "app.ico");
        var icoBytes = BuildIco(frames);
        File.WriteAllBytes(icoPath, icoBytes);
        Console.WriteLine("ICO written: " + icoPath + " (" + icoBytes.Length + " bytes)");

        var pngPath = Path.Combine(outDir, "app.png");
        using (var png = new Bitmap(256, 256))
        using (var g = Graphics.FromImage(png))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            TrayIconFactory.DrawIcon(g, 256, Color.FromArgb(22, 132, 125), false);
            png.Save(pngPath, ImageFormat.Png);
        }
        Console.WriteLine("PNG written: " + pngPath + " (" + new FileInfo(pngPath).Length + " bytes)");

        var expected = 6 + frames.Count * 16 + frames.Sum(f => f.dib.Length);
        Console.WriteLine($"expected total={expected}, actual={icoBytes.Length}, match={expected == icoBytes.Length}");
    }

    private static byte[] BitmapToDib(Bitmap bitmap)
    {
        // Convert to 32bpp ARGB DIB (BITMAPINFOHEADER + pixel data), no file header.
        var width = bitmap.Width;
        var height = bitmap.Height;
        var bmpData = bitmap.LockBits(
            new Rectangle(0, 0, width, height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            var stride = bmpData.Stride;
            var pixelBytes = stride * height;
            var dib = new byte[40 + pixelBytes];
            // BITMAPINFOHEADER
            WriteUInt32(dib, 0, 40);          // biSize
            WriteInt32(dib, 4, width);        // biWidth
            WriteInt32(dib, 8, height * 2);   // biHeight (doubled for icon AND mask)
            WriteUInt16(dib, 12, 1);          // biPlanes
            WriteUInt16(dib, 14, 32);         // biBitCount
            WriteUInt32(dib, 16, 0);          // biCompression (BI_RGB)
            WriteUInt32(dib, 20, (uint)pixelBytes);
            WriteUInt32(dib, 24, 0);          // biXPelsPerMeter
            WriteUInt32(dib, 28, 0);          // biYPelsPerMeter
            WriteUInt32(dib, 32, 0);          // biClrUsed
            WriteUInt32(dib, 36, 0);          // biClrImportant
            // Pixel data (DIB is bottom-up; bitmap is top-down via LockBits stride).
            System.Runtime.InteropServices.Marshal.Copy(bmpData.Scan0, dib, 40, pixelBytes);
            return dib;
        }
        finally
        {
            bitmap.UnlockBits(bmpData);
        }
    }

    private static byte[] BuildIco(List<(int size, byte[] dib)> frames)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write((ushort)0);
        bw.Write((ushort)1);
        bw.Write((ushort)frames.Count);
        var offset = 6 + frames.Count * 16;
        foreach (var (size, dib) in frames)
        {
            bw.Write((byte)(size >= 256 ? 0 : size));
            bw.Write((byte)(size >= 256 ? 0 : size));
            bw.Write((byte)0);
            bw.Write((byte)0);
            bw.Write((uint)dib.Length);
            bw.Write((uint)offset);
            offset += dib.Length;
        }

        foreach (var (_, dib) in frames)
        {
            bw.Write(dib);
        }

        bw.Flush();
        return ms.ToArray();
    }

    private static void WriteUInt16(byte[] b, int o, ushort v)
    {
        b[o] = (byte)(v & 0xff);
        b[o + 1] = (byte)((v >> 8) & 0xff);
    }

    private static void WriteUInt32(byte[] b, int o, uint v)
    {
        b[o] = (byte)(v & 0xff);
        b[o + 1] = (byte)((v >> 8) & 0xff);
        b[o + 2] = (byte)((v >> 16) & 0xff);
        b[o + 3] = (byte)((v >> 24) & 0xff);
    }

    private static void WriteInt32(byte[] b, int o, int v) => WriteUInt32(b, o, (uint)v);
}