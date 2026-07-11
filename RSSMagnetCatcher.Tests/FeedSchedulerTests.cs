using RSSMagnetCatcher.Core.Models;
using RSSMagnetCatcher.Core.Services;
using RSSMagnetCatcher.Infrastructure;
using RSSMagnetCatcher.Storage;

namespace RSSMagnetCatcher.Tests;

public sealed class FeedSchedulerTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "RRSMC.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void GetDueFeeds_ReturnsEnabledFeedsWithoutFutureNextCheck()
    {
        var now = DateTimeOffset.Parse("2026-06-01T10:00:00+08:00");
        var dueFeeds = FeedScheduler.GetDueFeeds(
        [
            new FeedConfig { Id = "never", Enabled = true },
            new FeedConfig { Id = "past", Enabled = true },
            new FeedConfig { Id = "future", Enabled = true },
            new FeedConfig { Id = "disabled", Enabled = false }
        ],
        new Dictionary<string, FeedState>
        {
            ["past"] = new() { NextCheckAt = now.AddMinutes(-1) },
            ["future"] = new() { NextCheckAt = now.AddMinutes(1) }
        },
        now);

        Assert.Equal(["never", "past"], dueFeeds.Select(feed => feed.Id));
    }

    [Fact]
    public async Task PauseAndResume_ControlDueChecks()
    {
        var checker = new CountingFeedCheckService();
        using var scheduler = CreateScheduler(checker);

        scheduler.Pause();
        Assert.True(scheduler.Snapshot.IsPaused);
        Assert.False((await scheduler.CheckDueNowAsync()).Started);

        scheduler.Resume();
        Assert.False(scheduler.Snapshot.IsPaused);
        Assert.True((await scheduler.CheckDueNowAsync()).Started);
        Assert.Equal(1, checker.CallCount);
    }

    [Fact]
    public async Task CheckAllNowAsync_PreventsOverlappingRuns()
    {
        var checker = new BlockingFeedCheckService();
        using var scheduler = CreateScheduler(checker);

        var firstRun = scheduler.CheckAllNowAsync(true);
        await checker.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var secondRun = await scheduler.CheckAllNowAsync(true);
        checker.Release.SetResult();

        Assert.False(secondRun.Started);
        Assert.True((await firstRun).Started);
    }

    [Fact]
    public async Task Start_RunsDueChecksAfterStartupDelay()
    {
        var checker = new SignalingFeedCheckService();
        using var scheduler = CreateScheduler(checker, new AppSettings
        {
            StartupCheckDelaySeconds = 0,
            RssRequestIntervalSeconds = 1
        });

        scheduler.Start();

        await checker.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(scheduler.Snapshot.IsStarted);
    }

    [Fact]
    public async Task CheckFailedNowAsync_RunsOnlyEnabledFailedFeeds()
    {
        var paths = new DataPaths(_tempDirectory);
        var configStore = new JsonConfigStore();
        var stateStore = new FeedStateStore(configStore, paths.FeedStateFile);
        stateStore.Set("failed", new FeedState { LastStatus = "failed" });
        stateStore.Set("ok", new FeedState { LastStatus = "ok" });
        var checker = new RecordingFeedCheckService();
        using var scheduler = new FeedScheduler(
            () =>
            [
                new FeedConfig { Id = "failed", Enabled = true },
                new FeedConfig { Id = "ok", Enabled = true },
                new FeedConfig { Id = "disabled", Enabled = false }
            ],
            stateStore,
            checker,
            new AppSettings { RssRequestIntervalSeconds = 1 },
            new Logger(paths.AppLogFile, paths.ErrorLogFile));

        var result = await scheduler.CheckFailedNowAsync(true);

        Assert.True(result.Started);
        Assert.Equal(["failed"], checker.FeedIds);
    }

    [Fact]
    public async Task BackfillMikanHistoryNowAsync_ForcesHistoryCapableChecker()
    {
        var checker = new HistoryRecordingFeedCheckService();
        using var scheduler = CreateScheduler(checker);

        var result = await scheduler.BackfillMikanHistoryNowAsync(new FeedConfig { Id = "mikan" });

        Assert.True(result.Started);
        Assert.True(checker.WasForced);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }

    private FeedScheduler CreateScheduler(IFeedCheckService checker, AppSettings? settings = null)
    {
        var paths = new DataPaths(_tempDirectory);
        var configStore = new JsonConfigStore();
        return new FeedScheduler(
            () => [new FeedConfig { Id = "feed_1", Enabled = true }],
            new FeedStateStore(configStore, paths.FeedStateFile),
            checker,
            settings ?? new AppSettings { RssRequestIntervalSeconds = 1 },
            new Logger(paths.AppLogFile, paths.ErrorLogFile));
    }

    private sealed class CountingFeedCheckService : IFeedCheckService
    {
        public int CallCount { get; private set; }

        public Task<FeedCheckResult> CheckFeedAsync(
            FeedConfig feed,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new FeedCheckResult { Feed = feed, Succeeded = true });
        }
    }

    private sealed class BlockingFeedCheckService : IFeedCheckService
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<FeedCheckResult> CheckFeedAsync(
            FeedConfig feed,
            CancellationToken cancellationToken = default)
        {
            Started.SetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return new FeedCheckResult { Feed = feed, Succeeded = true };
        }
    }

    private sealed class SignalingFeedCheckService : IFeedCheckService
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<FeedCheckResult> CheckFeedAsync(
            FeedConfig feed,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            return Task.FromResult(new FeedCheckResult { Feed = feed, Succeeded = true });
        }
    }

    private sealed class RecordingFeedCheckService : IFeedCheckService
    {
        public List<string> FeedIds { get; } = [];

        public Task<FeedCheckResult> CheckFeedAsync(
            FeedConfig feed,
            CancellationToken cancellationToken = default)
        {
            FeedIds.Add(feed.Id);
            return Task.FromResult(new FeedCheckResult { Feed = feed, Succeeded = true });
        }
    }

    private sealed class HistoryRecordingFeedCheckService : IHistoryBackfillFeedCheckService
    {
        public bool WasForced { get; private set; }

        public Task<FeedCheckResult> CheckFeedAsync(
            FeedConfig feed,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new FeedCheckResult { Feed = feed, Succeeded = true });
        }

        public Task<FeedCheckResult> CheckFeedAsync(
            FeedConfig feed,
            bool forceHistoryBackfill,
            CancellationToken cancellationToken = default)
        {
            WasForced = forceHistoryBackfill;
            return Task.FromResult(new FeedCheckResult { Feed = feed, Succeeded = true });
        }
    }
}
