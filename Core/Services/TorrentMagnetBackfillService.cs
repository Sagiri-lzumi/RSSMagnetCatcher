using RSSMagnetCatcher.Core.Models;
using RSSMagnetCatcher.Storage;

namespace RSSMagnetCatcher.Core.Services;

public sealed record TorrentMagnetBackfillResult(int UpgradedItems, int UpdatedFeeds);

public sealed class TorrentMagnetBackfillService
{
    private readonly JsonlItemStore _itemStore;
    private readonly FeedStateStore _stateStore;
    private readonly MagnetExtractService _extractService;
    private readonly ItemFilterService _itemFilterService;

    public TorrentMagnetBackfillService(
        JsonlItemStore itemStore,
        FeedStateStore stateStore,
        MagnetExtractService extractService,
        ItemFilterService itemFilterService)
    {
        _itemStore = itemStore;
        _stateStore = stateStore;
        _extractService = extractService;
        _itemFilterService = itemFilterService;
    }

    public TorrentMagnetBackfillResult Backfill(IEnumerable<FeedConfig> feeds)
    {
        var feedById = feeds.ToDictionary(feed => feed.Id, StringComparer.Ordinal);
        var knownHashes = _itemStore.LoadLatest()
            .Where(item => !string.IsNullOrWhiteSpace(item.InfoHash))
            .Select(item => item.InfoHash)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var upgradedFeedIds = new HashSet<string>(StringComparer.Ordinal);
        var upgradedItems = 0;

        foreach (var item in _itemStore.LoadLatest())
        {
            if (!string.Equals(item.MatchStatus, MatchStatuses.TorrentOnly, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(item.TorrentUrl)
                || !_extractService.TryCreateMagnetFromTorrentUrl(item.TorrentUrl, out var extracted)
                || !knownHashes.Add(extracted.InfoHash))
            {
                continue;
            }

            var isMatched = _itemFilterService.IsMatched(item);
            var feed = feedById.GetValueOrDefault(item.FeedId);
            item.Magnet = extracted.Magnet;
            item.InfoHash = extracted.InfoHash;
            item.IsChecked = false;
            item.IsExported = false;
            item.ProcessingStatus = ProcessingStatuses.Pending;
            item.MatchStatus = isMatched ? MatchStatuses.Extracted : MatchStatuses.Filtered;
            _itemStore.Append(item);
            upgradedFeedIds.Add(item.FeedId);
            upgradedItems++;
        }

        UpdateFeedStates(upgradedFeedIds);
        return new TorrentMagnetBackfillResult(upgradedItems, upgradedFeedIds.Count);
    }

    private void UpdateFeedStates(IEnumerable<string> upgradedFeedIds)
    {
        foreach (var feedId in upgradedFeedIds)
        {
            var items = _itemStore.LoadLatest()
                .Where(item => string.Equals(item.FeedId, feedId, StringComparison.Ordinal))
                .ToList();
            var magnetItems = items.Where(item => !string.IsNullOrWhiteSpace(item.Magnet)).ToList();
            var matchedCount = magnetItems.Count(_itemFilterService.IsMatched);
            var state = _stateStore.Load().GetValueOrDefault(feedId) ?? new FeedState();
            state.LastMagnetCount = magnetItems.Count;
            state.LastMatchedMagnetCount = matchedCount;
            state.LastNewCount = magnetItems.Count(item =>
                !item.IsExported
                && !string.Equals(item.ProcessingStatus, ProcessingStatuses.Deleted, StringComparison.Ordinal)
                && _itemFilterService.IsMatched(item));
            state.LastStatus = matchedCount > 0 ? "ok" : MatchStatuses.Filtered;
            state.LastErrorCategory = state.LastStatus == "ok" ? string.Empty : state.LastStatus;
            _stateStore.Set(feedId, state);
        }
    }
}
