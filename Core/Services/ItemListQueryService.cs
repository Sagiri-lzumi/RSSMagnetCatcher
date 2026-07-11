using RSSMagnetCatcher.Core.Models;

namespace RSSMagnetCatcher.Core.Services;

public sealed class ItemListQueryService
{
    public IReadOnlyList<MagnetItem> Query(
        IEnumerable<MagnetItem> items,
        ItemListQuery query,
        Func<MagnetItem, bool> isMatched)
    {
        var searchTerms = SplitSearchTerms(query.SearchText);
        var hasSearch = searchTerms.Count > 0;
        var isGlobalSearch = hasSearch && query.SearchScope == ItemSearchScope.AllItems;
        var includeDeleted = hasSearch && query.IncludeDeletedItems;

        return items
            .Where(item => includeDeleted
                || !string.Equals(item.ProcessingStatus, ProcessingStatuses.Deleted, StringComparison.Ordinal))
            .Where(item => isGlobalSearch || MatchesViewOrSearchDeleted(item, query, includeDeleted))
            .Where(item => !query.HideExportedItems || !item.IsExported)
            .Where(item => !ShouldApplyRuleFilter(query)
                || isGlobalSearch
                || !query.ShowOnlyMatchingItems
                || (!string.IsNullOrWhiteSpace(item.Magnet) && isMatched(item)))
            .Where(item => !hasSearch || MatchesSearch(item, query, searchTerms))
            .OrderByDescending(item => item.FoundAt)
            .ToList();
    }

    private static bool MatchesViewOrSearchDeleted(MagnetItem item, ItemListQuery query, bool includeDeleted)
    {
        if (string.Equals(item.ProcessingStatus, ProcessingStatuses.Deleted, StringComparison.Ordinal))
        {
            if (!includeDeleted)
            {
                return false;
            }

            return query.Mode switch
            {
                ItemListViewMode.Feed => item.FeedId == query.FeedId,
                ItemListViewMode.Failed => query.FailedFeedIds.Contains(item.FeedId),
                _ => true
            };
        }

        return MatchesView(item, query);
    }

    private static bool MatchesView(MagnetItem item, ItemListQuery query)
    {
        if (string.Equals(item.ProcessingStatus, ProcessingStatuses.Deleted, StringComparison.Ordinal))
        {
            return false;
        }

        return query.Mode switch
        {
            ItemListViewMode.Pending => IsExportable(item)
                && string.Equals(item.ProcessingStatus, ProcessingStatuses.Pending, StringComparison.Ordinal),
            ItemListViewMode.Unexported => IsExportable(item)
                && string.Equals(item.ProcessingStatus, ProcessingStatuses.Pending, StringComparison.Ordinal),
            ItemListViewMode.New => IsExportable(item)
                && string.Equals(item.ProcessingStatus, ProcessingStatuses.Pending, StringComparison.Ordinal),
            ItemListViewMode.Discarded => IsExportable(item)
                && string.Equals(item.ProcessingStatus, ProcessingStatuses.Discarded, StringComparison.Ordinal),
            ItemListViewMode.Used => IsExportable(item)
                && string.Equals(item.ProcessingStatus, ProcessingStatuses.Used, StringComparison.Ordinal),
            ItemListViewMode.Exceptions => !IsExportable(item)
                || (IsDiagnosticStatus(item.MatchStatus) && string.IsNullOrWhiteSpace(item.TorrentUrl)),
            ItemListViewMode.Failed => query.FailedFeedIds.Contains(item.FeedId),
            ItemListViewMode.Feed => item.FeedId == query.FeedId
                && IsExportable(item)
                && string.Equals(item.ProcessingStatus, ProcessingStatuses.Pending, StringComparison.Ordinal),
            _ => true
        };
    }

    private static bool ShouldApplyRuleFilter(ItemListQuery query)
    {
        return query.Mode is ItemListViewMode.All or ItemListViewMode.Unexported or ItemListViewMode.New;
    }

    private static bool IsExportable(MagnetItem item)
    {
        return !string.IsNullOrWhiteSpace(item.Magnet)
            || !string.IsNullOrWhiteSpace(item.TorrentUrl);
    }

    private static bool IsDiagnosticStatus(string status)
    {
        return status is MatchStatuses.NoMagnet or MatchStatuses.TorrentOnly;
    }

    private static IReadOnlyList<string> SplitSearchTerms(string? searchText)
    {
        return string.IsNullOrWhiteSpace(searchText)
            ? []
            : searchText
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
    }

    private static bool MatchesSearch(
        MagnetItem item,
        ItemListQuery query,
        IReadOnlyList<string> searchTerms)
    {
        var feedName = query.FeedNamesById?.GetValueOrDefault(item.FeedId) ?? string.Empty;
        var searchableText = string.Join(
            Environment.NewLine,
            item.Title,
            feedName,
            item.SearchText,
            item.InfoHash,
            item.Magnet,
            item.TorrentUrl,
            item.ProcessingStatus,
            item.MatchStatus);
        return searchTerms.All(term =>
            searchableText.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
