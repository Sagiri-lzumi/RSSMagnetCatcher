namespace RSSMagnetCatcher.Core.Models;

public sealed class FilterRule
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string IncludeExpression { get; set; } = string.Empty;

    public string ExcludeExpression { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public bool ShowAsQuickButton { get; set; } = true;
}
