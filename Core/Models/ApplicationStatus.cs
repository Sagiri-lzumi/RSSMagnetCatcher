namespace RSSMagnetCatcher.Core.Models;

public enum ApplicationState
{
    Normal,
    Checking,
    HasNew,
    PartialFailure,
    Offline,
    Paused
}

public sealed record ApplicationStatusSnapshot(
    ApplicationState State,
    int FeedCount,
    int FailedFeedCount,
    int NewCount,
    DateTimeOffset? NextCheckAt,
    int CompletedChecks,
    int TotalChecks);
