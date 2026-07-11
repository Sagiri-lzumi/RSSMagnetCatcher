using RSSMagnetCatcher.Core.Models;

namespace RSSMagnetCatcher.Core.Services;

public interface IFeedCheckService
{
    Task<FeedCheckResult> CheckFeedAsync(
        FeedConfig feed,
        CancellationToken cancellationToken = default);
}

public interface IHistoryBackfillFeedCheckService : IFeedCheckService
{
    Task<FeedCheckResult> CheckFeedAsync(
        FeedConfig feed,
        bool forceHistoryBackfill,
        CancellationToken cancellationToken = default);
}
