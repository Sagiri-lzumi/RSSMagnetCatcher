using RSSMagnetCatcher.Infrastructure;

namespace RSSMagnetCatcher.Tests;

public sealed class StartupManagerTests
{
    [Fact]
    public void BuildCommand_QuotesAbsoluteExecutablePath()
    {
        var executablePath = Path.Combine(Path.GetTempPath(), "RRSMC Portable", "RRSMC.exe");

        Assert.Equal($"\"{Path.GetFullPath(executablePath)}\"", StartupManager.BuildCommand(executablePath));
    }
}
