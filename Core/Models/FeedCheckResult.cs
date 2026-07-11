namespace RSSMagnetCatcher.Core.Models;

public sealed class FeedCheckResult
{
    public required FeedConfig Feed { get; init; }

    public bool Succeeded { get; init; }

    public int NewMagnetCount { get; init; }

    public int NewMatchedMagnetCount { get; init; }

    public int MagnetCount { get; init; }

    public int EntryCount { get; init; }

    public int HistoryBackfillEntryCount { get; init; }

    public int HistoryBackfillNewMagnetCount { get; init; }

    public string Warning { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;
}
