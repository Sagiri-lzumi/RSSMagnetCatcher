namespace RSSMagnetCatcher.Core.Models;

public enum ItemListViewMode
{
    All,
    Unexported,
    New,
    Pending,
    Discarded,
    Used,
    Exceptions,
    Failed,
    Feed
}

public enum ItemSearchScope
{
    CurrentView,
    AllItems
}

public sealed record ItemListQuery(
    ItemListViewMode Mode,
    string? FeedId,
    IReadOnlySet<string> FailedFeedIds,
    bool ShowOnlyMatchingItems,
    bool HideExportedItems,
    string SearchText = "",
    ItemSearchScope SearchScope = ItemSearchScope.CurrentView,
    bool IncludeDeletedItems = false,
    IReadOnlyDictionary<string, string>? FeedNamesById = null);
