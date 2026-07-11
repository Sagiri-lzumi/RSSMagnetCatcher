namespace RSSMagnetCatcher.Core.Models;

public sealed class FeedState
{
    public DateTimeOffset? LastCheckedAt { get; set; }

    public DateTimeOffset? NextCheckAt { get; set; }

    public string LastStatus { get; set; } = "not_checked";

    public int? HttpStatusCode { get; set; }

    public bool ParsedXml { get; set; }

    public bool HasEntries { get; set; }

    public int LastEntryCount { get; set; }

    public int LastRssEntryCount { get; set; }

    public int LastHistoryBackfillEntryCount { get; set; }

    public int CompletedHistoryBackfillTarget { get; set; }

    public DateTimeOffset? LastHistoryBackfillAt { get; set; }

    public string HistoryBackfillWarning { get; set; } = string.Empty;

    public int LastNewCount { get; set; }

    public int LastMagnetCount { get; set; }

    public int LastMatchedMagnetCount { get; set; }

    public int ConsecutiveFailCount { get; set; }

    public string LastErrorCategory { get; set; } = string.Empty;

    public string LastError { get; set; } = string.Empty;
}

public static class FeedDiagnosticCategories
{
    public const string NetworkFailure = "network_failure";
    public const string DnsFailure = "dns_failure";
    public const string HttpsFailure = "https_failure";
    public const string HttpError = "http_error";
    public const string NonXml = "non_xml";
    public const string XmlParseError = "xml_parse_error";
    public const string NoItems = "no_items";
    public const string NoMagnet = "no_magnet";
    public const string TorrentOnly = "torrent_only";
    public const string Filtered = "filtered";
    public const string UnknownFailure = "unknown_failure";
}
