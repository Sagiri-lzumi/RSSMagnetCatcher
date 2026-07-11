namespace RSSMagnetCatcher.Core.Models;

public static class MatchStatuses
{
    public const string Extracted = "extracted";
    public const string NoMagnet = "no_magnet";
    public const string TorrentOnly = "torrent_only";
    public const string Filtered = "filtered";
    public const string Exported = "exported";
}

public static class ProcessingStatuses
{
    public const string Pending = "pending";
    public const string Discarded = "discarded";
    public const string Used = "used";
    public const string Deleted = "deleted";
}

public sealed class MagnetItem
{
    public string Id { get; set; } = string.Empty;

    public string FeedId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Magnet { get; set; } = string.Empty;

    public string InfoHash { get; set; } = string.Empty;

    public string TorrentUrl { get; set; } = string.Empty;

    public string SearchText { get; set; } = string.Empty;

    public DateTimeOffset? PublishedAt { get; set; }

    public DateTimeOffset FoundAt { get; set; }

    public bool IsNew { get; set; }

    public bool IsChecked { get; set; }

    public bool IsExported { get; set; }

    public string MatchStatus { get; set; } = MatchStatuses.Extracted;

    public string ProcessingStatus { get; set; } = ProcessingStatuses.Pending;
}
