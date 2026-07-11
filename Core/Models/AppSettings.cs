namespace RSSMagnetCatcher.Core.Models;

public sealed class AppSettings
{
    public int GlobalIntervalMinutes { get; set; } = 30;

    public int StartupCheckDelaySeconds { get; set; } = 10;

    public int FailedRetryMinutes { get; set; } = 5;

    public int RssRequestIntervalSeconds { get; set; } = 2;

    public bool AutoStartWithWindows { get; set; }

    public bool StartMinimizedToTray { get; set; }

    public bool CheckAllOnStartup { get; set; }

    public bool CloseWindowToTray { get; set; } = true;

    public bool CopyAfterActionMarkExported { get; set; } = true;

    public bool HideExportedAfterCopy { get; set; }

    public int MaxCacheItems { get; set; } = 10000;

    public int MaxArticlesPerFeed { get; set; } = 1000;

    public int KeepHistoryDays { get; set; } = 90;

    public bool ShowOnlyMatchingItems { get; set; } = true;

    public string ClipboardLineEnding { get; set; } = "CRLF";

    public string DefaultRuleId { get; set; } = "rule_1080p_sc";

    public string? LastValidFilterExpression { get; set; }
}
