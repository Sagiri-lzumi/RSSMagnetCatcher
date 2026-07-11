namespace RSSMagnetCatcher.Core.Models;

public sealed record SchedulerSnapshot(
    bool IsStarted,
    bool IsPaused,
    bool IsChecking,
    int CompletedFeeds,
    int TotalFeeds)
{
    public static SchedulerSnapshot Stopped { get; } = new(false, false, false, 0, 0);
}

public sealed record SchedulerRunResult(
    bool Started,
    bool IsManual,
    IReadOnlyList<FeedCheckResult> Results);

public sealed class SchedulerRunCompletedEventArgs : EventArgs
{
    public SchedulerRunCompletedEventArgs(SchedulerRunResult result)
    {
        Result = result;
    }

    public SchedulerRunResult Result { get; }
}
