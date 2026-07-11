namespace RSSMagnetCatcher.Core.Models;

public sealed class FeedConfig
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public string Group { get; set; } = "默认";

    public bool UseGlobalInterval { get; set; } = true;

    public int IntervalMinutes { get; set; } = 30;

    public string DefaultRuleId { get; set; } = string.Empty;

    public bool AutoCheckNewMatchedItems { get; set; } = true;

    public bool? EnableMikanHistoryBackfill { get; set; }

    public override string ToString()
    {
        return Name;
    }
}
