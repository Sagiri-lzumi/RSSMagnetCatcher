using System.Diagnostics;

namespace RSSMagnetCatcher.Infrastructure;

public static class PathLauncher
{
    public static void OpenDirectory(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }
}
