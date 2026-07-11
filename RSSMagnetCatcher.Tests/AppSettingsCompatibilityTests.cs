using RSSMagnetCatcher.Core.Models;
using RSSMagnetCatcher.Storage;

namespace RSSMagnetCatcher.Tests;

public sealed class AppSettingsCompatibilityTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "RRSMC.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Load_OldSettingsUsesNewPropertyDefaults()
    {
        var path = Path.Combine(_tempDirectory, "app.settings.json");
        Directory.CreateDirectory(_tempDirectory);
        File.WriteAllText(path, """{"globalIntervalMinutes":45}""");

        var settings = new JsonConfigStore().Load(path, new AppSettings());

        Assert.Equal(45, settings.GlobalIntervalMinutes);
        Assert.True(settings.CloseWindowToTray);
        Assert.Equal("rule_1080p_sc", settings.DefaultRuleId);
        Assert.Equal(1000, settings.MaxArticlesPerFeed);
        Assert.True(settings.ShowOnlyMatchingItems);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }
}
