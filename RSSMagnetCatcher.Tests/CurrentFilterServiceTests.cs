using RSSMagnetCatcher.Core.Models;
using RSSMagnetCatcher.Core.Services;
using RSSMagnetCatcher.Storage;

namespace RSSMagnetCatcher.Tests;

public sealed class CurrentFilterServiceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "RRSMC.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Constructor_UsesFirstEnabledPresetWhenNoSavedExpressionExists()
    {
        var service = CreateService(new AppSettings());

        Assert.Equal("(CHS);!(720p)", service.CurrentExpression);
    }

    [Fact]
    public void TryApply_KeepsPreviousExpressionWhenRegexIsInvalid()
    {
        var settings = new AppSettings();
        var service = CreateService(settings);

        Assert.False(service.TryApply("(invalid", out _));
        Assert.Equal("(CHS);!(720p)", service.CurrentExpression);
    }

    [Fact]
    public void TryApply_PersistsValidExpression()
    {
        var settings = new AppSettings();
        var store = new JsonConfigStore();
        var path = Path.Combine(_tempDirectory, "app.settings.json");
        var service = CreateService(settings, store, path);

        Assert.True(service.TryApply("(1080p)", out _));

        Assert.Equal("(1080p)", store.Load(path, new AppSettings()).LastValidFilterExpression);
    }

    [Fact]
    public void Constructor_PreservesSavedEmptyExpressionInsteadOfReloadingPreset()
    {
        var settings = new AppSettings { LastValidFilterExpression = string.Empty };

        var service = CreateService(settings);

        Assert.Equal(string.Empty, service.CurrentExpression);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }

    private CurrentFilterService CreateService(
        AppSettings settings,
        JsonConfigStore? store = null,
        string? path = null)
    {
        return new CurrentFilterService(
            settings,
            store ?? new JsonConfigStore(),
            path ?? Path.Combine(_tempDirectory, "app.settings.json"),
            new RuleMatchService(),
            [
                new FilterRule
                {
                    IncludeExpression = "(CHS)",
                    ExcludeExpression = "720p"
                }
            ]);
    }
}
