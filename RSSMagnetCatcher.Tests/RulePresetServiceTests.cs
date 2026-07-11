using RSSMagnetCatcher.Core.Models;
using RSSMagnetCatcher.Core.Services;
using RSSMagnetCatcher.Storage;

namespace RSSMagnetCatcher.Tests;

public sealed class RulePresetServiceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "RRSMC.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void SaveAndDelete_ManagePresetLifecycle()
    {
        var service = new RulePresetService(
            new JsonConfigStore(),
            Path.Combine(_tempDirectory, "rules.json"),
            new RuleMatchService());
        var rule = service.Save(new FilterRule { Name = "1080p", IncludeExpression = "1080p" });

        rule.Enabled = false;
        service.Save(rule);

        Assert.False(Assert.Single(service.Load()).Enabled);
        Assert.True(service.Delete(rule.Id));
        Assert.Empty(service.Load());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }
}
