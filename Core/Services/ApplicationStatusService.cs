using RSSMagnetCatcher.Core.Models;

namespace RSSMagnetCatcher.Core.Services;

public sealed class ApplicationStatusService
{
    public ApplicationStatusSnapshot Calculate(
        IEnumerable<FeedConfig> feeds,
        IReadOnlyDictionary<string, FeedState> feedStates,
        IEnumerable<MagnetItem> items,
        SchedulerSnapshot scheduler)
    {
        var enabledFeeds = feeds.Where(feed => feed.Enabled).ToList();
        var failedFeedCount = enabledFeeds.Count(feed =>
            feedStates.TryGetValue(feed.Id, out var state)
            && string.Equals(state.LastStatus, "failed", StringComparison.Ordinal));
        var newCount = items.Count(item =>
            !string.IsNullOrWhiteSpace(item.Magnet)
            && string.Equals(item.ProcessingStatus, ProcessingStatuses.Pending, StringComparison.Ordinal));
        var nextCheckAt = enabledFeeds
            .Select(feed => feedStates.GetValueOrDefault(feed.Id)?.NextCheckAt)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .Order()
            .Cast<DateTimeOffset?>()
            .FirstOrDefault();

        var state = scheduler.IsPaused
            ? ApplicationState.Paused
            : scheduler.IsChecking
                ? ApplicationState.Checking
                : enabledFeeds.Count > 0 && failedFeedCount == enabledFeeds.Count
                    ? ApplicationState.Offline
                    : failedFeedCount > 0
                        ? ApplicationState.PartialFailure
                        : newCount > 0
                            ? ApplicationState.HasNew
                            : ApplicationState.Normal;

        return new ApplicationStatusSnapshot(
            state,
            enabledFeeds.Count,
            failedFeedCount,
            newCount,
            nextCheckAt,
            scheduler.CompletedFeeds,
            scheduler.TotalFeeds);
    }

    public string GetDisplayName(ApplicationState state)
    {
        return state switch
        {
            ApplicationState.Checking => "检查中",
            ApplicationState.HasNew => "有新增",
            ApplicationState.PartialFailure => "部分失败",
            ApplicationState.Offline => "离线",
            ApplicationState.Paused => "已暂停",
            _ => "正常"
        };
    }
}
