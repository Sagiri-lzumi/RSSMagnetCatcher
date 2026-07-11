using System.Net;
using System.Text.RegularExpressions;
using RSSMagnetCatcher.Core.Models;

namespace RSSMagnetCatcher.Core.Services;

public sealed record MikanHistoryFetchResult(
    IReadOnlyList<RssItem> Items,
    int PagesFetched,
    bool CompletedTarget,
    string Warning);

public sealed class MikanHistoryService
{
    private static readonly Regex RowRegex = new(
        "<tr[^>]*>(?<row>.*?)</tr>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(2));

    private static readonly Regex PublishedRegex = new(
        "<td[^>]*>(?<value>.*?)</td>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex EpisodeRegex = new(
        "href=\"(?<episode>/Home/Episode/[^\"]+)\"[^>]*class=\"magnet-link-wrap\"[^>]*>(?<title>.*?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex MagnetRegex = new(
        "data-clipboard-text=\"(?<magnet>magnet:[^\"]+)\"",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex TorrentRegex = new(
        "href=\"(?<torrent>[^\"]+\\.torrent(?:\\?[^\"]*)?)\"",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex TagRegex = new(
        "<[^>]+>",
        RegexOptions.Singleline | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private readonly RssFetchService _fetchService;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;

    public MikanHistoryService(
        RssFetchService fetchService,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        _fetchService = fetchService;
        _delayAsync = delayAsync ?? Task.Delay;
    }

    public static bool IsSupportedUrl(string? url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (string.Equals(uri.Host, "mikanani.me", StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Host, "mikanime.tv", StringComparison.OrdinalIgnoreCase))
            && string.Equals(uri.AbsolutePath.TrimEnd('/'), "/RSS/Classic", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsEnabled(FeedConfig feed)
    {
        return feed.EnableMikanHistoryBackfill ?? IsSupportedUrl(feed.Url);
    }

    public async Task<MikanHistoryFetchResult> FetchAsync(
        FeedConfig feed,
        int targetCount,
        int requestIntervalSeconds,
        CancellationToken cancellationToken = default)
    {
        if (!IsSupportedUrl(feed.Url) || !IsEnabled(feed))
        {
            return new MikanHistoryFetchResult([], 0, true, string.Empty);
        }

        var items = new List<RssItem>();
        var sourceKeys = new HashSet<string>(StringComparer.Ordinal);
        var pagesFetched = 0;
        try
        {
            for (var page = 1; items.Count < Math.Max(1, targetCount); page++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _delayAsync(
                    TimeSpan.FromSeconds(Math.Clamp(requestIntervalSeconds, 1, 3)),
                    cancellationToken);

                var fetched = await _fetchService.FetchAsync(BuildPageUrl(feed.Url, page), cancellationToken);
                pagesFetched++;
                var pageItems = ParsePage(fetched.Content, feed.Url);
                if (pageItems.Count == 0)
                {
                    break;
                }

                var added = 0;
                foreach (var item in pageItems)
                {
                    if (sourceKeys.Add(item.SourceKey))
                    {
                        items.Add(item);
                        added++;
                    }
                }

                if (added == 0)
                {
                    break;
                }
            }

            return new MikanHistoryFetchResult(items, pagesFetched, true, string.Empty);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new MikanHistoryFetchResult(
                items,
                pagesFetched,
                false,
                $"Mikan 历史补抓失败：{exception.Message}");
        }
    }

    public IReadOnlyList<RssItem> ParsePage(string html, string feedUrl)
    {
        var baseUri = new Uri(feedUrl);
        var items = new List<RssItem>();
        var sourceKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match rowMatch in RowRegex.Matches(html))
        {
            var row = rowMatch.Groups["row"].Value;
            var episode = EpisodeRegex.Match(row);
            var magnet = MagnetRegex.Match(row);
            if (!episode.Success || !magnet.Success)
            {
                continue;
            }

            var sourceKey = WebUtility.HtmlDecode(episode.Groups["episode"].Value);
            if (!sourceKeys.Add(sourceKey))
            {
                continue;
            }

            var title = CleanText(episode.Groups["title"].Value);
            var magnetValue = WebUtility.HtmlDecode(magnet.Groups["magnet"].Value);
            var torrentMatch = TorrentRegex.Match(row);
            var torrentUrl = torrentMatch.Success
                ? new Uri(baseUri, WebUtility.HtmlDecode(torrentMatch.Groups["torrent"].Value)).AbsoluteUri
                : string.Empty;
            var publishedMatch = PublishedRegex.Match(row);

            items.Add(new RssItem
            {
                SourceKey = sourceKey,
                Title = title,
                PublishedAt = publishedMatch.Success ? ParsePublishedAt(CleanText(publishedMatch.Groups["value"].Value)) : null,
                CandidateTexts = [title, magnetValue, torrentUrl, sourceKey]
            });
        }

        return items;
    }

    private static string BuildPageUrl(string feedUrl, int page)
    {
        var uri = new Uri(feedUrl);
        return $"{uri.Scheme}://{uri.Authority}/Home/Classic/{page}";
    }

    private static string CleanText(string value)
    {
        return WebUtility.HtmlDecode(TagRegex.Replace(value, string.Empty)).Trim();
    }

    private static DateTimeOffset? ParsePublishedAt(string value)
    {
        var now = DateTimeOffset.Now;
        if (value.StartsWith("今天 ", StringComparison.Ordinal)
            && TimeOnly.TryParse(value[3..], out var todayTime))
        {
            return new DateTimeOffset(now.Date.Add(todayTime.ToTimeSpan()), now.Offset);
        }

        if (value.StartsWith("昨天 ", StringComparison.Ordinal)
            && TimeOnly.TryParse(value[3..], out var yesterdayTime))
        {
            return new DateTimeOffset(now.Date.AddDays(-1).Add(yesterdayTime.ToTimeSpan()), now.Offset);
        }

        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }
}
