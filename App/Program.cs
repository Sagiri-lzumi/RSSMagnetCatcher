using System.Drawing;
using System.Drawing.Drawing2D;

namespace RSSMagnetCatcher.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length > 0 && string.Equals(args[0], "--generate-icon", StringComparison.OrdinalIgnoreCase))
        {
            var outDir = args.Length > 1 ? args[1] : ".";
            IconGenerator.GenerateTo(outDir);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(AppBootstrapper.Build());
    }
}