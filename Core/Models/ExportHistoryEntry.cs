namespace RSSMagnetCatcher.Core.Models;

public static class ExportKinds
{
    public const string Magnet = "magnet";
    public const string Torrent = "torrent";
}

public sealed class ExportHistoryEntry
{
    public string ItemId { get; set; } = string.Empty;

    public string InfoHash { get; set; } = string.Empty;

    public string Magnet { get; set; } = string.Empty;

    public string ExportKind { get; set; } = ExportKinds.Magnet;

    public string TorrentUrl { get; set; } = string.Empty;

    public string FilePath { get; set; } = string.Empty;

    public DateTimeOffset ExportedAt { get; set; }
}
