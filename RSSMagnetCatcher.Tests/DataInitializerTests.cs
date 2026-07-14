using System.Text.Json;
using RSSMagnetCatcher.Core.Models;
using RSSMagnetCatcher.Storage;

namespace RSSMagnetCatcher.Tests;

public sealed class DataInitializerTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "RRSMC.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Initialize_CreatesPortableDataFilesWithNoDefaultFeeds()
    {
        var paths = new DataPaths(_tempDirectory);
        var configStore = new JsonConfigStore();

        new DataInitializer(paths, configStore).Initialize();

        Assert.True(File.Exists(paths.SettingsFile));
        Assert.True(File.Exists(paths.RulesFile));
        Assert.True(File.Exists(paths.FeedStateFile));
        Assert.True(File.Exists(paths.ItemCacheFile));
        Assert.True(File.Exists(paths.ExportHistoryFile));
        Assert.True(File.Exists(paths.ActiveBatchFile));
        Assert.True(File.Exists(paths.AppLogFile));
        Assert.True(File.Exists(paths.ErrorLogFile));
        Assert.True(Directory.Exists(paths.TorrentExportDirectory));

        Assert.True(File.Exists(paths.FeedsFile));
        var feeds = configStore.Load(paths.FeedsFile, new List<FeedConfig>());
        Assert.Empty(feeds);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }
}
