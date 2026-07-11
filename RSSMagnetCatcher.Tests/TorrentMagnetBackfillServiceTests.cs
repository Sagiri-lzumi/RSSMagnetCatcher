using RSSMagnetCatcher.Core.Models;
using RSSMagnetCatcher.Core.Services;
using RSSMagnetCatcher.Storage;

namespace RSSMagnetCatcher.Tests;

public sealed class TorrentMagnetBackfillServiceTests : IDisposable
{
    private const string Hash = "3b1b057bc76a806ca14108ce0a2cbb378a900f32";
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "RRSMC.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Backfill_UpgradesOriginalSnapshotAndSynchronizesFeedState()
    {
        var (service, itemStore, stateStore) = CreateService("(CHS);(1080p)");
        itemStore.Append(CreateTorrentOnlyItem("item_existing", "Example CHS 1080p"));
        stateStore.Set("feed_1", new FeedState
        {
            LastStatus = MatchStatuses.TorrentOnly,
            LastMagnetCount = 0
        });

        var result = service.Backfill(
        [
            new FeedConfig { Id = "feed_1", Enabled = true, AutoCheckNewMatchedItems = true }
        ]);

        Assert.Equal(1, result.UpgradedItems);
        var item = Assert.Single(itemStore.LoadLatest());
        Assert.Equal("item_existing", item.Id);
        Assert.Equal(Hash, item.InfoHash);
        Assert.False(item.IsChecked);
        Assert.False(item.IsExported);
        Assert.Equal(MatchStatuses.Extracted, item.MatchStatus);
        Assert.Equal(ProcessingStatuses.Pending, item.ProcessingStatus);
        var state = stateStore.Load()["feed_1"];
        Assert.Equal("ok", state.LastStatus);
        Assert.Equal(1, state.LastMagnetCount);
        Assert.Equal(1, state.LastMatchedMagnetCount);
    }

    [Fact]
    public void Backfill_KeepsFilteredUpgradeUnchecked()
    {
        var (service, itemStore, _) = CreateService("(CHS);(1080p)");
        itemStore.Append(CreateTorrentOnlyItem("item_filtered", "Example BIG5 720p"));

        var result = service.Backfill(
        [
            new FeedConfig { Id = "feed_1", Enabled = true, AutoCheckNewMatchedItems = true }
        ]);

        Assert.Equal(1, result.UpgradedItems);
        var item = Assert.Single(itemStore.LoadLatest());
        Assert.False(item.IsChecked);
        Assert.Equal(MatchStatuses.Filtered, item.MatchStatus);
        Assert.Equal(ProcessingStatuses.Pending, item.ProcessingStatus);
    }

    [Fact]
    public void Backfill_DoesNotDuplicateKnownInfoHash()
    {
        var (service, itemStore, _) = CreateService(string.Empty);
        itemStore.Append(new MagnetItem
        {
            Id = "item_magnet",
            Magnet = $"magnet:?xt=urn:btih:{Hash}",
            InfoHash = Hash,
            FoundAt = DateTimeOffset.Now
        });
        itemStore.Append(CreateTorrentOnlyItem("item_torrent", "Duplicate"));

        Assert.Equal(0, service.Backfill([]).UpgradedItems);
        Assert.Equal(2, itemStore.LoadLatest().Count);
        Assert.Equal(MatchStatuses.TorrentOnly, itemStore.LoadLatest().Single(item => item.Id == "item_torrent").MatchStatus);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }

    private (TorrentMagnetBackfillService Service, JsonlItemStore ItemStore, FeedStateStore StateStore)
        CreateService(string filter)
    {
        var paths = new DataPaths(_tempDirectory);
        var configStore = new JsonConfigStore();
        var itemStore = new JsonlItemStore(paths.ItemCacheFile);
        var stateStore = new FeedStateStore(configStore, paths.FeedStateFile);
        var currentFilter = new CurrentFilterService(
            new AppSettings { LastValidFilterExpression = filter },
            configStore,
            paths.SettingsFile,
            new RuleMatchService(),
            []);
        var itemFilter = new ItemFilterService(itemStore, new RuleMatchService(), currentFilter);
        return (
            new TorrentMagnetBackfillService(itemStore, stateStore, new MagnetExtractService(), itemFilter),
            itemStore,
            stateStore);
    }

    private static MagnetItem CreateTorrentOnlyItem(string id, string title)
    {
        return new MagnetItem
        {
            Id = id,
            FeedId = "feed_1",
            Title = title,
            TorrentUrl = $"https://mikanani.me/Download/20260602/{Hash}.torrent",
            SearchText = title,
            FoundAt = DateTimeOffset.Now,
            IsNew = true,
            MatchStatus = MatchStatuses.TorrentOnly
        };
    }
}
