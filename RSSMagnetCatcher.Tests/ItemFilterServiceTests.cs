using RSSMagnetCatcher.Core.Models;
using RSSMagnetCatcher.Core.Services;
using RSSMagnetCatcher.Storage;

namespace RSSMagnetCatcher.Tests;

public sealed class ItemFilterServiceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "RRSMC.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void ReevaluateAll_UsesTitleFallbackForOldCacheAndKeepsManualChecks()
    {
        var itemStore = new JsonlItemStore(Path.Combine(_tempDirectory, "item_cache.jsonl"));
        itemStore.Append(new MagnetItem
        {
            Id = "item_old",
            Title = "Example BIG5 720p",
            Magnet = "magnet:?xt=urn:btih:AAA111",
            InfoHash = "aaa111",
            FoundAt = DateTimeOffset.Now,
            IsChecked = true
        });
        var currentFilter = new CurrentFilterService(
            new AppSettings { LastValidFilterExpression = "(CHS);(1080p)" },
            new JsonConfigStore(),
            Path.Combine(_tempDirectory, "settings.json"),
            new RuleMatchService(),
            []);
        var service = new ItemFilterService(itemStore, new RuleMatchService(), currentFilter);

        Assert.Equal(1, service.ReevaluateAll());

        var latest = Assert.Single(itemStore.LoadLatest());
        Assert.Equal(MatchStatuses.Filtered, latest.MatchStatus);
        Assert.True(latest.IsChecked);
    }

    [Fact]
    public void ReevaluateAll_RestoresExtractedStatusWhenConditionMatchesAgain()
    {
        var itemStore = new JsonlItemStore(Path.Combine(_tempDirectory, "item_cache.jsonl"));
        itemStore.Append(new MagnetItem
        {
            Id = "item_filtered",
            Title = "Example CHS 1080p",
            Magnet = "magnet:?xt=urn:btih:AAA111",
            InfoHash = "aaa111",
            FoundAt = DateTimeOffset.Now,
            MatchStatus = MatchStatuses.Filtered
        });
        var currentFilter = new CurrentFilterService(
            new AppSettings { LastValidFilterExpression = "(CHS);(1080p)" },
            new JsonConfigStore(),
            Path.Combine(_tempDirectory, "settings.json"),
            new RuleMatchService(),
            []);
        var service = new ItemFilterService(itemStore, new RuleMatchService(), currentFilter);

        Assert.Equal(1, service.ReevaluateAll());

        Assert.Equal(MatchStatuses.Extracted, Assert.Single(itemStore.LoadLatest()).MatchStatus);
    }

    [Fact]
    public void SetChecked_UpdatesOnlyEligibleItemsAndPersistsLatestSnapshots()
    {
        var itemStore = new JsonlItemStore(Path.Combine(_tempDirectory, "item_cache.jsonl"));
        var matched = CreateItem("matched", "Example CHS 1080p", "aaa111");
        var filtered = CreateItem("filtered", "Example BIG5 720p", "bbb222");
        var exported = CreateItem("exported", "Example CHS 1080p", "ccc333");
        exported.IsExported = true;
        var noMagnet = CreateItem("no_magnet", "Example CHS 1080p", string.Empty);
        foreach (var item in new[] { matched, filtered, exported, noMagnet })
        {
            itemStore.Append(item);
        }

        var currentFilter = new CurrentFilterService(
            new AppSettings { LastValidFilterExpression = "(CHS);(1080p)" },
            new JsonConfigStore(),
            Path.Combine(_tempDirectory, "settings.json"),
            new RuleMatchService(),
            []);
        var service = new ItemFilterService(itemStore, new RuleMatchService(), currentFilter);

        Assert.Equal(1, service.SetChecked([matched, filtered, exported, noMagnet], true));
        Assert.True(itemStore.LoadLatest().Single(item => item.Id == "matched").IsChecked);

        Assert.Equal(1, service.SetChecked([matched], false));
        Assert.False(itemStore.LoadLatest().Single(item => item.Id == "matched").IsChecked);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }

    private static MagnetItem CreateItem(string id, string title, string infoHash)
    {
        return new MagnetItem
        {
            Id = id,
            Title = title,
            Magnet = string.IsNullOrWhiteSpace(infoHash) ? string.Empty : $"magnet:?xt=urn:btih:{infoHash}",
            InfoHash = infoHash,
            SearchText = title,
            FoundAt = DateTimeOffset.Now
        };
    }
}
