using RSSMagnetCatcher.Core.Models;
using RSSMagnetCatcher.Core.Services;

namespace RSSMagnetCatcher.Tests;

public sealed class ItemListQueryServiceTests
{
    [Fact]
    public void Query_WhenShowOnlyMatchingIsEnabled_HidesFilteredAndStatusRows()
    {
        var service = new ItemListQueryService();
        var result = service.Query(
        [
            CreateItem("matched", "feed_a", "CHS 1080p", "aaa111"),
            CreateItem("filtered", "feed_a", "BIG5 720p", "bbb222"),
            CreateItem("no_magnet", "feed_a", "CHS 1080p", string.Empty)
        ],
            new ItemListQuery(ItemListViewMode.All, null, new HashSet<string>(), true, false),
            item => item.Title.Contains("CHS", StringComparison.Ordinal));

        Assert.Equal("matched", Assert.Single(result).Id);
    }

    [Fact]
    public void Query_FeedViewShowsOnlyPendingItemsForThatFeed()
    {
        var service = new ItemListQueryService();
        var result = service.Query(
        [
            CreateItem("feed_a", "feed_a", "CHS 1080p", "aaa111"),
            CreateItem("feed_b", "feed_b", "BIG5 720p", "bbb222"),
            CreateItem("no_magnet", "feed_b", "status", string.Empty)
        ],
            new ItemListQuery(ItemListViewMode.Feed, "feed_b", new HashSet<string>(), false, false),
            _ => false);

        Assert.Equal(["feed_b"], result.Select(item => item.Id).Order());
    }

    [Fact]
    public void Query_ExceptionsViewShowsDiagnosticRows()
    {
        var service = new ItemListQueryService();
        var result = service.Query(
        [
            CreateItem("pending", "feed_a", "CHS 1080p", "aaa111"),
            CreateItem("no_magnet", "feed_a", "status", string.Empty)
        ],
            new ItemListQuery(ItemListViewMode.Exceptions, null, new HashSet<string>(), false, false),
            _ => false);

        Assert.Equal("no_magnet", Assert.Single(result).Id);
    }

    [Fact]
    public void Query_PendingViewIncludesTorrentExportableItems()
    {
        var service = new ItemListQueryService();
        var torrentOnly = CreateItem("torrent_only", "feed_a", "status", string.Empty);
        torrentOnly.MatchStatus = MatchStatuses.TorrentOnly;
        torrentOnly.TorrentUrl = "https://example.test/files/torrent_only.torrent";

        var pending = service.Query(
            [torrentOnly],
            new ItemListQuery(ItemListViewMode.Pending, null, new HashSet<string>(), false, false),
            _ => false);
        var exceptions = service.Query(
            [torrentOnly],
            new ItemListQuery(ItemListViewMode.Exceptions, null, new HashSet<string>(), false, false),
            _ => false);

        Assert.Equal("torrent_only", Assert.Single(pending).Id);
        Assert.Empty(exceptions);
    }

    [Fact]
    public void Query_WorkspacesAreSeparatedByProcessingStatusAndSortedNewestFirst()
    {
        var service = new ItemListQueryService();
        var olderPending = CreateItem("older_pending", "feed_a", "CHS 1080p", "aaa111");
        olderPending.FoundAt = DateTimeOffset.Parse("2026-06-01T10:00:00+08:00");
        var newerPending = CreateItem("newer_pending", "feed_a", "CHS 1080p", "bbb222");
        newerPending.FoundAt = DateTimeOffset.Parse("2026-06-02T10:00:00+08:00");
        var discarded = CreateItem("discarded", "feed_a", "CHS 1080p", "ccc333");
        discarded.ProcessingStatus = ProcessingStatuses.Discarded;
        var used = CreateItem("used", "feed_a", "CHS 1080p", "ddd444");
        used.ProcessingStatus = ProcessingStatuses.Used;
        used.IsExported = true;

        var pending = service.Query(
            [olderPending, newerPending, discarded, used],
            new ItemListQuery(ItemListViewMode.Pending, null, new HashSet<string>(), false, false),
            _ => true);
        var discardedResult = service.Query(
            [olderPending, newerPending, discarded, used],
            new ItemListQuery(ItemListViewMode.Discarded, null, new HashSet<string>(), false, false),
            _ => true);

        Assert.Equal(["newer_pending", "older_pending"], pending.Select(item => item.Id));
        Assert.Equal("discarded", Assert.Single(discardedResult).Id);
    }

    [Fact]
    public void Query_SearchesOnlyCurrentViewByDefault()
    {
        var service = new ItemListQueryService();
        var pending = CreateItem("pending", "feed_a", "Alpha CHS", "aaa111");
        var used = CreateItem("used", "feed_a", "Alpha used", "bbb222");
        used.ProcessingStatus = ProcessingStatuses.Used;
        used.IsExported = true;

        var result = service.Query(
            [pending, used],
            new ItemListQuery(
                ItemListViewMode.Pending,
                null,
                new HashSet<string>(),
                false,
                false,
                SearchText: "alpha"),
            _ => true);

        Assert.Equal("pending", Assert.Single(result).Id);
    }

    [Fact]
    public void Query_GlobalSearchIgnoresCurrentViewAndSearchesFeedName()
    {
        var service = new ItemListQueryService();
        var pending = CreateItem("pending", "feed_a", "One", "aaa111");
        var used = CreateItem("used", "feed_b", "Two", "bbb222");
        used.ProcessingStatus = ProcessingStatuses.Used;
        used.IsExported = true;

        var result = service.Query(
            [pending, used],
            new ItemListQuery(
                ItemListViewMode.Pending,
                null,
                new HashSet<string>(),
                false,
                false,
                SearchText: "archive",
                SearchScope: ItemSearchScope.AllItems,
                FeedNamesById: new Dictionary<string, string> { ["feed_b"] = "Archive Feed" }),
            _ => false);

        Assert.Equal("used", Assert.Single(result).Id);
    }

    [Fact]
    public void Query_SearchIncludesDeletedOnlyWhenRequestedAndSearchTextExists()
    {
        var service = new ItemListQueryService();
        var deleted = CreateItem("deleted", "feed_a", "Needle", "aaa111");
        deleted.ProcessingStatus = ProcessingStatuses.Deleted;

        var withoutDeleted = service.Query(
            [deleted],
            new ItemListQuery(
                ItemListViewMode.Pending,
                null,
                new HashSet<string>(),
                false,
                false,
                SearchText: "needle",
                SearchScope: ItemSearchScope.AllItems),
            _ => true);
        var withDeleted = service.Query(
            [deleted],
            new ItemListQuery(
                ItemListViewMode.Pending,
                null,
                new HashSet<string>(),
                false,
                false,
                SearchText: "needle",
                SearchScope: ItemSearchScope.AllItems,
                IncludeDeletedItems: true),
            _ => true);
        var emptySearch = service.Query(
            [deleted],
            new ItemListQuery(
                ItemListViewMode.Pending,
                null,
                new HashSet<string>(),
                false,
                false,
                IncludeDeletedItems: true),
            _ => true);

        Assert.Empty(withoutDeleted);
        Assert.Equal("deleted", Assert.Single(withDeleted).Id);
        Assert.Empty(emptySearch);
    }

    [Fact]
    public void Query_CurrentFeedSearchCanIncludeDeletedItemsFromSameFeed()
    {
        var service = new ItemListQueryService();
        var sameFeed = CreateItem("same_feed_deleted", "feed_a", "Needle", "aaa111");
        sameFeed.ProcessingStatus = ProcessingStatuses.Deleted;
        var otherFeed = CreateItem("other_feed_deleted", "feed_b", "Needle", "bbb222");
        otherFeed.ProcessingStatus = ProcessingStatuses.Deleted;

        var result = service.Query(
            [sameFeed, otherFeed],
            new ItemListQuery(
                ItemListViewMode.Feed,
                "feed_a",
                new HashSet<string>(),
                false,
                false,
                SearchText: "needle",
                IncludeDeletedItems: true),
            _ => true);

        Assert.Equal("same_feed_deleted", Assert.Single(result).Id);
    }

    [Fact]
    public void Query_SearchUsesAndTermsAndMatchesTechnicalFields()
    {
        var service = new ItemListQueryService();
        var item = CreateItem("item", "feed_a", "Quiet title", "ABC123");
        item.SearchText = "Fansub HEVC 1080p";
        item.Magnet = "magnet:?xt=urn:btih:ABC123";
        item.TorrentUrl = "https://example.test/files/demo.torrent";

        var result = service.Query(
            [item],
            new ItemListQuery(
                ItemListViewMode.Pending,
                null,
                new HashSet<string>(),
                false,
                false,
                SearchText: "hevc abc123 torrent"),
            _ => true);

        Assert.Equal("item", Assert.Single(result).Id);
    }

    private static MagnetItem CreateItem(string id, string feedId, string title, string infoHash)
    {
        return new MagnetItem
        {
            Id = id,
            FeedId = feedId,
            Title = title,
            SearchText = title,
            InfoHash = infoHash,
            Magnet = string.IsNullOrWhiteSpace(infoHash) ? string.Empty : $"magnet:?xt=urn:btih:{infoHash}",
            FoundAt = DateTimeOffset.Now
        };
    }
}
