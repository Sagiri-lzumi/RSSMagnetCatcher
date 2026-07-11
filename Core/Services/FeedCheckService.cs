using RSSMagnetCatcher.Core.Models;
using RSSMagnetCatcher.Core.Utils;
using RSSMagnetCatcher.Infrastructure;
using RSSMagnetCatcher.Storage;

namespace RSSMagnetCatcher.Core.Services;

public sealed class FeedCheckService : IHistoryBackfillFeedCheckService
{
    private readonly RssFetchService _fetchService;
    private readonly RssParseService _parseService;
    private readonly MagnetExtractService _extractService;
    private readonly JsonlItemStore _itemStore;
    private readonly FeedStateStore _stateStore;
    private readonly Logger _logger;
    private readonly AppSettings _settings;
    private readonly RuleMatchService _ruleMatchService;
    private readonly CurrentFilterService _currentFilterService;
    private readonly FeedDiagnosticsService _diagnosticsService;
    private readonly MikanHistoryService _mikanHistoryService;

    public FeedCheckService(
        RssFetchService fetchService,
        RssParseService parseService,
        MagnetExtractService extractService,
        JsonlItemStore itemStore,
        FeedStateStore stateStore,
        Logger logger,
        AppSettings settings,
        RuleMatchService ruleMatchService,
        CurrentFilterService currentFilterService,
        FeedDiagnosticsService diagnosticsService,
        MikanHistoryService? mikanHistoryService = null)
    {
        _fetchService = fetchService;
        _parseService = parseService;
        _extractService = extractService;
        _itemStore = itemStore;
        _stateStore = stateStore;
        _logger = logger;
        _settings = settings;
        _ruleMatchService = ruleMatchService;
        _currentFilterService = currentFilterService;
        _diagnosticsService = diagnosticsService;
        _mikanHistoryService = mikanHistoryService ?? new MikanHistoryService(fetchService);
    }

    public async Task<IReadOnlyList<FeedCheckResult>> CheckEnabledFeedsAsync(
        IEnumerable<FeedConfig> feeds,
        CancellationToken cancellationToken = default)
    {
        var results = new List<FeedCheckResult>();
        foreach (var feed in feeds.Where(feed => feed.Enabled))
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await CheckFeedAsync(feed, false, cancellationToken));
        }

        return results;
    }

    public async Task<FeedCheckResult> CheckFeedAsync(
        FeedConfig feed,
        bool forceHistoryBackfill = false,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.Now;
        string? responseContent = null;
        int? httpStatusCode = null;

        try
        {
            _logger.Info($"开始检查 RSS：{feed.Name} ({feed.Url})");
            var fetched = await _fetchService.FetchAsync(feed.Url, cancellationToken);
            responseContent = fetched.Content;
            httpStatusCode = fetched.HttpStatusCode;
            var rssItems = _parseService.Parse(fetched.Content);
            var previousState = _stateStore.Load().GetValueOrDefault(feed.Id) ?? new FeedState();
            var historyTarget = Math.Clamp(_settings.MaxArticlesPerFeed, 100, 10000);
            var shouldBackfillHistory = MikanHistoryService.IsEnabled(feed)
                && (forceHistoryBackfill || previousState.CompletedHistoryBackfillTarget < historyTarget);
            var historyResult = shouldBackfillHistory
                ? await _mikanHistoryService.FetchAsync(
                    feed,
                    historyTarget,
                    _settings.RssRequestIntervalSeconds,
                    cancellationToken)
                : new MikanHistoryFetchResult([], 0, true, string.Empty);
            var existingItems = _itemStore.LoadLatest();
            var existingByHash = existingItems
                .Where(item => !string.IsNullOrWhiteSpace(item.InfoHash))
                .GroupBy(item => item.InfoHash, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.OrdinalIgnoreCase);
            var knownHashes = existingItems
                .Where(item => !string.IsNullOrWhiteSpace(item.InfoHash))
                .Select(item => item.InfoHash)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var knownIds = existingItems
                .Select(item => item.Id)
                .ToHashSet(StringComparer.Ordinal);

            var newMagnetCount = 0;
            var newMatchedMagnetCount = 0;
            var magnetCount = 0;
            var matchedMagnetCount = 0;
            var hasTorrentOnly = false;

            void ProcessItem(RssItem rssItem)
            {
                var extraction = _extractService.Extract(rssItem.CandidateTexts);
                magnetCount += extraction.Magnets.Count;
                if (extraction.Magnets.Count > 0
                    && _ruleMatchService.IsMatch(
                        _currentFilterService.CurrentExpression,
                        string.Join(Environment.NewLine, rssItem.CandidateTexts)))
                {
                    matchedMagnetCount += extraction.Magnets.Count;
                }

                if (extraction.Magnets.Count > 0)
                {
                    var primaryTorrentUrl = extraction.TorrentUrls.FirstOrDefault() ?? string.Empty;
                    foreach (var extracted in extraction.Magnets)
                    {
                        var torrentUrl = string.IsNullOrWhiteSpace(extracted.SourceTorrentUrl)
                            ? primaryTorrentUrl
                            : extracted.SourceTorrentUrl;
                        if (!knownHashes.Add(extracted.InfoHash))
                        {
                            BackfillExistingTorrentUrl(existingByHash, extracted.InfoHash, torrentUrl);
                            continue;
                        }

                        var item = CreateMagnetItem(feed, rssItem, extracted, now, primaryTorrentUrl);
                        _itemStore.Append(item);
                        existingByHash[item.InfoHash] = item;
                        knownIds.Add(item.Id);
                        newMagnetCount++;
                        if (item.MatchStatus == MatchStatuses.Extracted)
                        {
                            newMatchedMagnetCount++;
                        }
                    }

                    return;
                }

                var status = extraction.TorrentUrls.Count > 0
                    ? MatchStatuses.TorrentOnly
                    : MatchStatuses.NoMagnet;
                hasTorrentOnly |= status == MatchStatuses.TorrentOnly;
                var statusItem = CreateStatusItem(feed, rssItem, status, extraction.TorrentUrls.FirstOrDefault(), now);

                if (knownIds.Add(statusItem.Id))
                {
                    _itemStore.Append(statusItem);
                }
            }

            foreach (var rssItem in rssItems)
            {
                ProcessItem(rssItem);
            }

            var newMagnetCountBeforeHistory = newMagnetCount;
            foreach (var historyItem in historyResult.Items)
            {
                ProcessItem(historyItem);
            }

            var historyBackfillNewMagnetCount = newMagnetCount - newMagnetCountBeforeHistory;

            var stateStatus = rssItems.Count == 0
                ? "no_items"
                : magnetCount > 0
                    ? matchedMagnetCount > 0
                        ? "ok"
                        : MatchStatuses.Filtered
                    : hasTorrentOnly
                        ? MatchStatuses.TorrentOnly
                        : MatchStatuses.NoMagnet;

            _stateStore.Set(feed.Id, new FeedState
            {
                LastCheckedAt = now,
                NextCheckAt = now.AddMinutes(GetIntervalMinutes(feed)),
                LastStatus = stateStatus,
                HttpStatusCode = fetched.HttpStatusCode,
                ParsedXml = true,
                HasEntries = rssItems.Count > 0,
                LastEntryCount = rssItems.Count,
                LastRssEntryCount = rssItems.Count,
                LastHistoryBackfillEntryCount = shouldBackfillHistory
                    ? historyResult.Items.Count
                    : previousState.LastHistoryBackfillEntryCount,
                CompletedHistoryBackfillTarget = shouldBackfillHistory && historyResult.CompletedTarget
                    ? Math.Max(previousState.CompletedHistoryBackfillTarget, historyTarget)
                    : previousState.CompletedHistoryBackfillTarget,
                LastHistoryBackfillAt = shouldBackfillHistory
                    ? now
                    : previousState.LastHistoryBackfillAt,
                HistoryBackfillWarning = shouldBackfillHistory
                    ? historyResult.Warning
                    : previousState.HistoryBackfillWarning,
                LastNewCount = newMatchedMagnetCount,
                LastMagnetCount = magnetCount,
                LastMatchedMagnetCount = matchedMagnetCount,
                LastErrorCategory = stateStatus == "ok" ? string.Empty : stateStatus
            });

            var message = BuildSuccessMessage(rssItems.Count, magnetCount, hasTorrentOnly);
            if (historyResult.Items.Count > 0)
            {
                message += $"；历史补抓 {historyResult.Items.Count} 条";
            }

            if (!string.IsNullOrWhiteSpace(historyResult.Warning))
            {
                _logger.Error($"{feed.Name} {historyResult.Warning}");
            }

            _logger.Info($"{feed.Name} 检查完成：{message}，新增 {newMagnetCount} 条。");

            return new FeedCheckResult
            {
                Feed = feed,
                Succeeded = true,
                NewMagnetCount = newMagnetCount,
                NewMatchedMagnetCount = newMatchedMagnetCount,
                MagnetCount = magnetCount,
                EntryCount = rssItems.Count,
                HistoryBackfillEntryCount = historyResult.Items.Count,
                HistoryBackfillNewMagnetCount = historyBackfillNewMagnetCount,
                Warning = historyResult.Warning,
                Message = message
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var previousState = _stateStore.Load().GetValueOrDefault(feed.Id) ?? new FeedState();
            httpStatusCode = exception is RssFetchException fetchException
                ? (int?)fetchException.StatusCode
                : httpStatusCode;

            _stateStore.Set(feed.Id, new FeedState
            {
                LastCheckedAt = now,
                NextCheckAt = now.AddMinutes(Math.Clamp(_settings.FailedRetryMinutes, 1, 10080)),
                LastStatus = "failed",
                HttpStatusCode = httpStatusCode,
                ParsedXml = false,
                HasEntries = false,
                ConsecutiveFailCount = previousState.ConsecutiveFailCount + 1,
                LastErrorCategory = _diagnosticsService.ClassifyFailure(exception, responseContent),
                LastError = exception.Message,
                LastHistoryBackfillEntryCount = previousState.LastHistoryBackfillEntryCount,
                CompletedHistoryBackfillTarget = previousState.CompletedHistoryBackfillTarget,
                LastHistoryBackfillAt = previousState.LastHistoryBackfillAt,
                HistoryBackfillWarning = previousState.HistoryBackfillWarning
            });
            _logger.Error($"{feed.Name} 检查失败：{exception.Message}", exception);

            return new FeedCheckResult
            {
                Feed = feed,
                Succeeded = false,
                Message = exception.Message
            };
        }
    }

    Task<FeedCheckResult> IFeedCheckService.CheckFeedAsync(
        FeedConfig feed,
        CancellationToken cancellationToken)
    {
        return CheckFeedAsync(feed, false, cancellationToken);
    }

    private int GetIntervalMinutes(FeedConfig feed)
    {
        return Math.Clamp(feed.UseGlobalInterval ? _settings.GlobalIntervalMinutes : feed.IntervalMinutes, 1, 10080);
    }

    private MagnetItem CreateMagnetItem(
        FeedConfig feed,
        RssItem rssItem,
        ExtractedMagnet extracted,
        DateTimeOffset now,
        string primaryTorrentUrl)
    {
        var searchText = string.Join(Environment.NewLine, rssItem.CandidateTexts);
        var isMatched = _ruleMatchService.IsMatch(_currentFilterService.CurrentExpression, searchText);
        var torrentUrl = string.IsNullOrWhiteSpace(extracted.SourceTorrentUrl)
            ? primaryTorrentUrl
            : extracted.SourceTorrentUrl;

        return new MagnetItem
        {
            Id = $"item_{HashHelper.Sha256Hex($"magnet|{extracted.InfoHash}")}",
            FeedId = feed.Id,
            Title = rssItem.Title,
            Magnet = extracted.Magnet,
            InfoHash = extracted.InfoHash,
            TorrentUrl = torrentUrl,
            SearchText = searchText,
            PublishedAt = rssItem.PublishedAt,
            FoundAt = now,
            IsNew = true,
            IsChecked = false,
            MatchStatus = isMatched ? MatchStatuses.Extracted : MatchStatuses.Filtered,
            ProcessingStatus = ProcessingStatuses.Pending
        };
    }

    private void BackfillExistingTorrentUrl(
        IDictionary<string, MagnetItem> existingByHash,
        string infoHash,
        string torrentUrl)
    {
        if (string.IsNullOrWhiteSpace(torrentUrl)
            || !existingByHash.TryGetValue(infoHash, out var existing)
            || !string.IsNullOrWhiteSpace(existing.TorrentUrl))
        {
            return;
        }

        existing.TorrentUrl = torrentUrl;
        _itemStore.Append(existing);
        existingByHash[infoHash] = existing;
    }

    private static MagnetItem CreateStatusItem(
        FeedConfig feed,
        RssItem rssItem,
        string status,
        string? torrentUrl,
        DateTimeOffset now)
    {
        var sourceKey = string.IsNullOrWhiteSpace(rssItem.SourceKey)
            ? rssItem.Title
            : rssItem.SourceKey;

        return new MagnetItem
        {
            Id = $"item_{HashHelper.Sha256Hex($"status|{feed.Id}|{sourceKey}")}",
            FeedId = feed.Id,
            Title = rssItem.Title,
            TorrentUrl = torrentUrl ?? string.Empty,
            SearchText = string.Join(Environment.NewLine, rssItem.CandidateTexts),
            PublishedAt = rssItem.PublishedAt,
            FoundAt = now,
            IsNew = true,
            MatchStatus = status,
            ProcessingStatus = ProcessingStatuses.Pending
        };
    }

    private static string BuildSuccessMessage(int entryCount, int magnetCount, bool hasTorrentOnly)
    {
        if (entryCount == 0)
        {
            return "RSS 正常，但没有 item/entry";
        }

        if (magnetCount > 0)
        {
            return $"发现 {magnetCount} 条 magnet";
        }

        return hasTorrentOnly
            ? "RSS 正常，但未发现 magnet；发现 torrent 链接"
            : "RSS 正常，但未发现 magnet";
    }
}
