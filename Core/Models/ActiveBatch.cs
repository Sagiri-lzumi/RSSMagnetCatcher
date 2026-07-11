namespace RSSMagnetCatcher.Core.Models;

public static class BatchSelectionModes
{
    public const string All = "all";
    public const string RuleMatched = "rule_matched";
}

public sealed class ActiveBatch
{
    public bool IsActive { get; set; }

    public string Id { get; set; } = string.Empty;

    public string SourceMode { get; set; } = string.Empty;

    public string? FeedId { get; set; }

    public string SourceProcessingStatus { get; set; } = ProcessingStatuses.Pending;

    public string SelectionMode { get; set; } = BatchSelectionModes.RuleMatched;

    public DateTimeOffset CreatedAt { get; set; }

    public List<string> ItemIds { get; set; } = [];

    public Dictionary<string, bool> OriginalCheckedByItemId { get; set; } = new(StringComparer.Ordinal);

    public static ActiveBatch Empty()
    {
        return new ActiveBatch();
    }
}
