using RSSMagnetCatcher.Core.Models;
using RSSMagnetCatcher.Infrastructure;
using RSSMagnetCatcher.Storage;

namespace RSSMagnetCatcher.Core.Services;

public sealed class FeedScheduler : IDisposable
{
    private readonly Func<IReadOnlyList<FeedConfig>> _feedsProvider;
    private readonly FeedStateStore _feedStateStore;
    private readonly IFeedCheckService _feedCheckService;
    private readonly AppSettings _settings;
    private readonly Logger _logger;
    private readonly SemaphoreSlim _runGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _backgroundTask;
    private SchedulerSnapshot _snapshot = SchedulerSnapshot.Stopped;

    public FeedScheduler(
        Func<IReadOnlyList<FeedConfig>> feedsProvider,
        FeedStateStore feedStateStore,
        IFeedCheckService feedCheckService,
        AppSettings settings,
        Logger logger)
    {
        _feedsProvider = feedsProvider;
        _feedStateStore = feedStateStore;
        _feedCheckService = feedCheckService;
        _settings = settings;
        _logger = logger;
    }

    public event EventHandler? StateChanged;

    public event EventHandler<SchedulerRunCompletedEventArgs>? RunCompleted;

    public SchedulerSnapshot Snapshot => _snapshot;

    public void Start()
    {
        if (_backgroundTask is not null)
        {
            return;
        }

        SetSnapshot(_snapshot with { IsStarted = true });
        _backgroundTask = Task.Run(() => RunBackgroundAsync(_shutdown.Token));
    }

    public void Pause()
    {
        SetSnapshot(_snapshot with { IsPaused = true });
    }

    public void Resume()
    {
        SetSnapshot(_snapshot with { IsPaused = false });
    }

    public Task<SchedulerRunResult> CheckAllNowAsync(
        bool isManual,
        CancellationToken cancellationToken = default)
    {
        return RunFeedsAsync(_feedsProvider().Where(feed => feed.Enabled).ToList(), isManual, cancellationToken);
    }

    public Task<SchedulerRunResult> CheckDueNowAsync(
        CancellationToken cancellationToken = default)
    {
        if (_snapshot.IsPaused)
        {
            return Task.FromResult(new SchedulerRunResult(false, false, []));
        }

        var feeds = GetDueFeeds(_feedsProvider(), _feedStateStore.Load(), DateTimeOffset.Now);
        return RunFeedsAsync(feeds, false, cancellationToken);
    }

    public Task<SchedulerRunResult> CheckFailedNowAsync(
        bool isManual,
        CancellationToken cancellationToken = default)
    {
        var feeds = GetFailedFeeds(_feedsProvider(), _feedStateStore.Load());
        return RunFeedsAsync(feeds, isManual, cancellationToken);
    }

    public Task<SchedulerRunResult> BackfillMikanHistoryNowAsync(
        FeedConfig feed,
        CancellationToken cancellationToken = default)
    {
        return RunFeedsAsync([feed], true, cancellationToken, true);
    }

    public static IReadOnlyList<FeedConfig> GetDueFeeds(
        IEnumerable<FeedConfig> feeds,
        IReadOnlyDictionary<string, FeedState> feedStates,
        DateTimeOffset now)
    {
        return feeds
            .Where(feed => feed.Enabled)
            .Where(feed =>
                !feedStates.TryGetValue(feed.Id, out var state)
                || !state.NextCheckAt.HasValue
                || state.NextCheckAt.Value <= now)
            .ToList();
    }

    public static IReadOnlyList<FeedConfig> GetFailedFeeds(
        IEnumerable<FeedConfig> feeds,
        IReadOnlyDictionary<string, FeedState> feedStates)
    {
        return feeds
            .Where(feed => feed.Enabled)
            .Where(feed =>
                feedStates.TryGetValue(feed.Id, out var state)
                && string.Equals(state.LastStatus, "failed", StringComparison.Ordinal))
            .ToList();
    }

    public void Dispose()
    {
        _shutdown.Cancel();
    }

    private async Task RunBackgroundAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(
                TimeSpan.FromSeconds(Math.Max(0, _settings.StartupCheckDelaySeconds)),
                cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                if (!_snapshot.IsPaused)
                {
                    await CheckDueNowAsync(cancellationToken);
                }

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.Error("后台 RSS 调度器异常退出。", exception);
        }
    }

    private async Task<SchedulerRunResult> RunFeedsAsync(
        IReadOnlyList<FeedConfig> feeds,
        bool isManual,
        CancellationToken cancellationToken,
        bool forceHistoryBackfill = false)
    {
        if (feeds.Count == 0 || !await _runGate.WaitAsync(0, cancellationToken))
        {
            return new SchedulerRunResult(false, isManual, []);
        }

        var results = new List<FeedCheckResult>();
        try
        {
            SetSnapshot(_snapshot with
            {
                IsChecking = true,
                CompletedFeeds = 0,
                TotalFeeds = feeds.Count
            });

            for (var index = 0; index < feeds.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                results.Add(forceHistoryBackfill
                    && _feedCheckService is IHistoryBackfillFeedCheckService historyBackfillService
                    ? await historyBackfillService.CheckFeedAsync(feeds[index], true, cancellationToken)
                    : await _feedCheckService.CheckFeedAsync(feeds[index], cancellationToken));
                SetSnapshot(_snapshot with { CompletedFeeds = index + 1 });

                if (index < feeds.Count - 1)
                {
                    await Task.Delay(
                        TimeSpan.FromSeconds(Math.Clamp(_settings.RssRequestIntervalSeconds, 1, 3)),
                        cancellationToken);
                }
            }
        }
        finally
        {
            SetSnapshot(_snapshot with { IsChecking = false });
            _runGate.Release();
        }

        var result = new SchedulerRunResult(true, isManual, results);
        RunCompleted?.Invoke(this, new SchedulerRunCompletedEventArgs(result));
        return result;
    }

    private void SetSnapshot(SchedulerSnapshot snapshot)
    {
        _snapshot = snapshot;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
