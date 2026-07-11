namespace RSSMagnetCatcher.Core.Models;

public sealed class RssItem
{
    public string SourceKey { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public DateTimeOffset? PublishedAt { get; init; }

    public IReadOnlyList<string> CandidateTexts { get; init; } = [];
}
