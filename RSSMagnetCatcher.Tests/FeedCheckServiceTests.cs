using System.Net;
using System.Text;
using RSSMagnetCatcher.Core.Models;
using RSSMagnetCatcher.Core.Services;
using RSSMagnetCatcher.Infrastructure;
using RSSMagnetCatcher.Storage;

namespace RSSMagnetCatcher.Tests;

public sealed class FeedCheckServiceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "RRSMC.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CheckFeedAsync_AddsNewMagnetsAsPendingAndRecordsRuleStatus()
    {
        const string xml = """
            <rss>
              <channel>
                <item>
                  <title>Example CHS 1080p</title>
                  <description>magnet:?xt=urn:btih:AAA111</description>
                </item>
                <item>
                  <title>Example BIG5 720p</title>
                  <description>magnet:?xt=urn:btih:BBB222</description>
                </item>
              </channel>
            </rss>
            """;
        var paths = new DataPaths(_tempDirectory);
        var configStore = new JsonConfigStore();
        new DataInitializer(paths, configStore).Initialize();
        var settings = new AppSettings { LastValidFilterExpression = "(CHS);(1080p)" };
        var ruleMatchService = new RuleMatchService();
        var currentFilter = new CurrentFilterService(
            settings,
            configStore,
            paths.SettingsFile,
            ruleMatchService,
            []);
        var itemStore = new JsonlItemStore(paths.ItemCacheFile);
        using var httpClient = new HttpClient(new StaticResponseHandler(xml));
        var service = new FeedCheckService(
            new RssFetchService(httpClient),
            new RssParseService(),
            new MagnetExtractService(),
            itemStore,
            new FeedStateStore(configStore, paths.FeedStateFile),
            new Logger(paths.AppLogFile, paths.ErrorLogFile),
            settings,
            ruleMatchService,
            currentFilter,
            new FeedDiagnosticsService());

        var result = await service.CheckFeedAsync(new FeedConfig
        {
            Id = "feed_1",
            Name = "Feed 1",
            Url = "https://example.test/rss"
        });

        Assert.Equal(2, result.NewMagnetCount);
        Assert.Equal(1, result.NewMatchedMagnetCount);
        var items = itemStore.LoadLatest();
        Assert.Contains(items, item =>
            item.InfoHash == "aaa111"
            && !item.IsChecked
            && item.ProcessingStatus == ProcessingStatuses.Pending
            && item.MatchStatus == MatchStatuses.Extracted);
        Assert.Contains(items, item =>
            item.InfoHash == "bbb222"
            && !item.IsChecked
            && item.ProcessingStatus == ProcessingStatuses.Pending
            && item.MatchStatus == MatchStatuses.Filtered);
    }

    [Fact]
    public async Task CheckFeedAsync_ClassifiesNonRssXml()
    {
        var paths = new DataPaths(_tempDirectory);
        var configStore = new JsonConfigStore();
        new DataInitializer(paths, configStore).Initialize();
        var settings = new AppSettings();
        var ruleMatchService = new RuleMatchService();
        var currentFilter = new CurrentFilterService(
            settings,
            configStore,
            paths.SettingsFile,
            ruleMatchService,
            []);
        using var httpClient = new HttpClient(new StaticResponseHandler("<html><body>login</body></html>"));
        var stateStore = new FeedStateStore(configStore, paths.FeedStateFile);
        var service = new FeedCheckService(
            new RssFetchService(httpClient),
            new RssParseService(),
            new MagnetExtractService(),
            new JsonlItemStore(paths.ItemCacheFile),
            stateStore,
            new Logger(paths.AppLogFile, paths.ErrorLogFile),
            settings,
            ruleMatchService,
            currentFilter,
            new FeedDiagnosticsService());

        var result = await service.CheckFeedAsync(new FeedConfig
        {
            Id = "feed_html",
            Name = "HTML",
            Url = "https://example.test/login"
        });

        Assert.False(result.Succeeded);
        Assert.Equal(FeedDiagnosticCategories.NonXml, stateStore.Load()["feed_html"].LastErrorCategory);
    }

    [Fact]
    public async Task CheckFeedAsync_UsesFeedSpecificIntervalWhenConfigured()
    {
        var paths = new DataPaths(_tempDirectory);
        var configStore = new JsonConfigStore();
        new DataInitializer(paths, configStore).Initialize();
        var settings = new AppSettings { GlobalIntervalMinutes = 30 };
        var ruleMatchService = new RuleMatchService();
        var currentFilter = new CurrentFilterService(
            settings,
            configStore,
            paths.SettingsFile,
            ruleMatchService,
            []);
        using var httpClient = new HttpClient(new StaticResponseHandler("<rss><channel /></rss>"));
        var stateStore = new FeedStateStore(configStore, paths.FeedStateFile);
        var service = new FeedCheckService(
            new RssFetchService(httpClient),
            new RssParseService(),
            new MagnetExtractService(),
            new JsonlItemStore(paths.ItemCacheFile),
            stateStore,
            new Logger(paths.AppLogFile, paths.ErrorLogFile),
            settings,
            ruleMatchService,
            currentFilter,
            new FeedDiagnosticsService());

        await service.CheckFeedAsync(new FeedConfig
        {
            Id = "feed_custom",
            Name = "Custom interval",
            Url = "https://example.test/rss",
            UseGlobalInterval = false,
            IntervalMinutes = 60
        });

        var state = stateStore.Load()["feed_custom"];
        Assert.Equal(TimeSpan.FromMinutes(60), state.NextCheckAt - state.LastCheckedAt);
    }

    [Fact]
    public async Task CheckFeedAsync_InfersExportableMagnetFromMikanTorrentEnclosure()
    {
        const string hash = "3b1b057bc76a806ca14108ce0a2cbb378a900f32";
        const string xml = """
            <rss version="2.0">
              <channel>
                <item>
                  <title>Example CHS 1080p</title>
                  <enclosure type="application/x-bittorrent"
                    url="https://mikanani.me/Download/20260602/3b1b057bc76a806ca14108ce0a2cbb378a900f32.torrent" />
                </item>
              </channel>
            </rss>
            """;
        var paths = new DataPaths(_tempDirectory);
        var configStore = new JsonConfigStore();
        new DataInitializer(paths, configStore).Initialize();
        var settings = new AppSettings { LastValidFilterExpression = "(CHS);(1080p)" };
        var ruleMatchService = new RuleMatchService();
        var currentFilter = new CurrentFilterService(
            settings,
            configStore,
            paths.SettingsFile,
            ruleMatchService,
            []);
        var itemStore = new JsonlItemStore(paths.ItemCacheFile);
        var stateStore = new FeedStateStore(configStore, paths.FeedStateFile);
        using var httpClient = new HttpClient(new StaticResponseHandler(xml));
        var service = new FeedCheckService(
            new RssFetchService(httpClient),
            new RssParseService(),
            new MagnetExtractService(),
            itemStore,
            stateStore,
            new Logger(paths.AppLogFile, paths.ErrorLogFile),
            settings,
            ruleMatchService,
            currentFilter,
            new FeedDiagnosticsService());

        var result = await service.CheckFeedAsync(new FeedConfig
        {
            Id = "feed_mikan",
            Name = "Mikan",
            Url = "https://mikanani.me/RSS/Classic"
        });

        Assert.Equal(1, result.MagnetCount);
        Assert.Equal(1, result.NewMatchedMagnetCount);
        var item = Assert.Single(itemStore.LoadLatest());
        Assert.Equal(hash, item.InfoHash);
        Assert.Equal("https://mikanani.me/Download/20260602/3b1b057bc76a806ca14108ce0a2cbb378a900f32.torrent", item.TorrentUrl);
        Assert.False(item.IsChecked);
        Assert.Equal(ProcessingStatuses.Pending, item.ProcessingStatus);
        Assert.Equal(1, stateStore.Load()["feed_mikan"].LastMagnetCount);
    }

    [Fact]
    public async Task CheckFeedAsync_BackfillsTorrentUrlForExistingMagnetItem()
    {
        const string hash = "3b1b057bc76a806ca14108ce0a2cbb378a900f32";
        const string torrentUrl = "https://mikanani.me/Download/20260602/3b1b057bc76a806ca14108ce0a2cbb378a900f32.torrent";
        const string xml = $"""
            <rss version="2.0">
              <channel>
                <item>
                  <title>Example CHS 1080p</title>
                  <description>magnet:?xt=urn:btih:{hash}</description>
                  <enclosure type="application/x-bittorrent" url="{torrentUrl}" />
                </item>
              </channel>
            </rss>
            """;
        var paths = new DataPaths(_tempDirectory);
        var configStore = new JsonConfigStore();
        new DataInitializer(paths, configStore).Initialize();
        var settings = new AppSettings();
        var ruleMatchService = new RuleMatchService();
        var currentFilter = new CurrentFilterService(
            settings,
            configStore,
            paths.SettingsFile,
            ruleMatchService,
            []);
        var itemStore = new JsonlItemStore(paths.ItemCacheFile);
        itemStore.Append(new MagnetItem
        {
            Id = "existing",
            FeedId = "feed_mikan",
            Title = "Existing",
            Magnet = $"magnet:?xt=urn:btih:{hash}",
            InfoHash = hash,
            FoundAt = DateTimeOffset.Parse("2026-06-03T10:00:00+08:00"),
            ProcessingStatus = ProcessingStatuses.Pending
        });
        using var httpClient = new HttpClient(new StaticResponseHandler(xml));
        var service = new FeedCheckService(
            new RssFetchService(httpClient),
            new RssParseService(),
            new MagnetExtractService(),
            itemStore,
            new FeedStateStore(configStore, paths.FeedStateFile),
            new Logger(paths.AppLogFile, paths.ErrorLogFile),
            settings,
            ruleMatchService,
            currentFilter,
            new FeedDiagnosticsService());

        await service.CheckFeedAsync(new FeedConfig
        {
            Id = "feed_mikan",
            Name = "Mikan",
            Url = "https://mikanani.me/RSS/Classic"
        });

        var item = Assert.Single(itemStore.LoadLatest());
        Assert.Equal("existing", item.Id);
        Assert.Equal(torrentUrl, item.TorrentUrl);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }

    private sealed class StaticResponseHandler : HttpMessageHandler
    {
        private readonly string _content;

        public StaticResponseHandler(string content)
        {
            _content = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_content, Encoding.UTF8, "application/xml")
            });
        }
    }
}
