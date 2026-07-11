using RSSMagnetCatcher.Core.Models;
using RSSMagnetCatcher.Storage;

namespace RSSMagnetCatcher.Tests;

public sealed class JsonlItemStoreTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "RRSMC.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void LoadLatest_ReturnsLastSnapshotForEachItem()
    {
        var store = new JsonlItemStore(Path.Combine(_tempDirectory, "item_cache.jsonl"));
        var item = new MagnetItem
        {
            Id = "item_1",
            FeedId = "feed_1",
            Magnet = "magnet:?xt=urn:btih:AAA111",
            InfoHash = "aaa111",
            FoundAt = DateTimeOffset.Parse("2026-06-01T10:00:00+08:00"),
            IsChecked = true
        };

        store.Append(item);
        item.IsChecked = false;
        item.IsExported = true;
        store.Append(item);

        var latest = Assert.Single(store.LoadLatest());
        Assert.False(latest.IsChecked);
        Assert.True(latest.IsExported);
    }

    [Fact]
    public void Rewrite_ReplacesSnapshotsWithProvidedLatestItems()
    {
        var path = Path.Combine(_tempDirectory, "item_cache.jsonl");
        var store = new JsonlItemStore(path);
        store.Append(new MagnetItem { Id = "old", FoundAt = DateTimeOffset.Now });

        store.Rewrite([new MagnetItem { Id = "retained", FoundAt = DateTimeOffset.Now }]);

        Assert.Equal("retained", Assert.Single(store.LoadLatest()).Id);
        Assert.Single(File.ReadLines(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }
}
