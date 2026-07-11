using RSSMagnetCatcher.Core.Models;
using RSSMagnetCatcher.Core.Services;

namespace RSSMagnetCatcher.Tests;

public sealed class ApplicationStatusServiceTests
{
    private readonly ApplicationStatusService _service = new();
    private readonly List<FeedConfig> _feeds =
    [
        new() { Id = "feed_1", Enabled = true },
        new() { Id = "feed_2", Enabled = true }
    ];

    [Fact]
    public void Calculate_UsesRequiredPriorityOrder()
    {
        var failedStates = new Dictionary<string, FeedState>
        {
            ["feed_1"] = new() { LastStatus = "failed" },
            ["feed_2"] = new() { LastStatus = "failed" }
        };
        var newItems = new[]
        {
            new MagnetItem { Magnet = "magnet:?xt=urn:btih:AAA111", MatchStatus = MatchStatuses.Extracted }
        };

        Assert.Equal(
            ApplicationState.Paused,
            _service.Calculate(_feeds, failedStates, newItems, new SchedulerSnapshot(true, true, true, 0, 2)).State);
        Assert.Equal(
            ApplicationState.Checking,
            _service.Calculate(_feeds, failedStates, newItems, new SchedulerSnapshot(true, false, true, 0, 2)).State);
        Assert.Equal(
            ApplicationState.Offline,
            _service.Calculate(_feeds, failedStates, newItems, SchedulerSnapshot.Stopped).State);
        Assert.Equal(
            ApplicationState.PartialFailure,
            _service.Calculate(
                _feeds,
                new Dictionary<string, FeedState> { ["feed_1"] = new() { LastStatus = "failed" } },
                newItems,
                SchedulerSnapshot.Stopped).State);
        Assert.Equal(
            ApplicationState.HasNew,
            _service.Calculate(_feeds, new Dictionary<string, FeedState>(), newItems, SchedulerSnapshot.Stopped).State);
        Assert.Equal(
            ApplicationState.Normal,
            _service.Calculate(_feeds, new Dictionary<string, FeedState>(), [], SchedulerSnapshot.Stopped).State);
    }
}
