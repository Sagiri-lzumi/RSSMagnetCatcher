using System.Net;
using System.Text;
using RSSMagnetCatcher.Core.Models;
using RSSMagnetCatcher.Core.Services;
using RSSMagnetCatcher.Infrastructure;
using RSSMagnetCatcher.Storage;

namespace RSSMagnetCatcher.Tests;

public sealed class FeedCheckMikanHistoryTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "RRSMC.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CheckFeedAsync_BackfillsMikanHistoryOnFirstCheckAndStoresCompletedTarget()
    {
        var handler = new RoutingHandler(uri => uri.AbsolutePath switch
        {
            "/RSS/Classic" => Rss("aaa111"),
            "/Home/Classic/1" => HtmlPage(HtmlRow("aaa111"), HtmlRow("bbb222")),
            _ => HtmlPage()
        });
        using var context = CreateContext(handler, new AppSettings { MaxArticlesPerFeed = 100 });

        var result = await context.Service.CheckFeedAsync(CreateMikanFeed());

        Assert.Equal(2, result.NewMagnetCount);
        Assert.Equal(2, result.HistoryBackfillEntryCount);
        Assert.Equal(1, result.HistoryBackfillNewMagnetCount);
        var state = context.StateStore.Load()["feed_mikan"];
        Assert.Equal(100, state.CompletedHistoryBackfillTarget);
        Assert.Equal(1, state.LastRssEntryCount);
        Assert.Equal(2, state.LastHistoryBackfillEntryCount);
        Assert.Contains(handler.Requests, uri => uri.AbsolutePath == "/Home/Classic/1");
    }

    [Fact]
    public async Task CheckFeedAsync_SkipsAutomaticHistoryWhenTargetWasAlreadyCompleted()
    {
        var handler = new RoutingHandler(_ => Rss("aaa111"));
        using var context = CreateContext(handler, new AppSettings { MaxArticlesPerFeed = 100 });
        context.StateStore.Set("feed_mikan", new FeedState { CompletedHistoryBackfillTarget = 100 });

        await context.Service.CheckFeedAsync(CreateMikanFeed());

        Assert.Single(handler.Requests);
        Assert.Equal("/RSS/Classic", handler.Requests[0].AbsolutePath);
    }

    [Fact]
    public async Task CheckFeedAsync_KeepsRssSuccessWhenHistoryBackfillFails()
    {
        var handler = new RoutingHandler(uri => uri.AbsolutePath == "/RSS/Classic"
            ? Rss("aaa111")
            : throw new HttpRequestException("history unavailable"));
        using var context = CreateContext(handler, new AppSettings { MaxArticlesPerFeed = 100 });

        var result = await context.Service.CheckFeedAsync(CreateMikanFeed());

        Assert.True(result.Succeeded);
        Assert.Contains("history unavailable", result.Warning);
        Assert.Contains("history unavailable", context.StateStore.Load()["feed_mikan"].HistoryBackfillWarning);
        Assert.Equal("ok", context.StateStore.Load()["feed_mikan"].LastStatus);
    }

    [Fact]
    public async Task CheckFeedAsync_BackfillsAgainWhenTargetIncreases()
    {
        var handler = new RoutingHandler(uri => uri.AbsolutePath == "/RSS/Classic"
            ? Rss("aaa111")
            : HtmlPage(HtmlRow("bbb222")));
        using var context = CreateContext(handler, new AppSettings { MaxArticlesPerFeed = 200 });
        context.StateStore.Set("feed_mikan", new FeedState { CompletedHistoryBackfillTarget = 100 });

        await context.Service.CheckFeedAsync(CreateMikanFeed());

        Assert.Contains(handler.Requests, uri => uri.AbsolutePath == "/Home/Classic/1");
        Assert.Equal(200, context.StateStore.Load()["feed_mikan"].CompletedHistoryBackfillTarget);
    }

    [Fact]
    public async Task CheckFeedAsync_DoesNotRequestMikanPagesForOrdinaryRss()
    {
        var handler = new RoutingHandler(_ => Rss("aaa111"));
        using var context = CreateContext(handler, new AppSettings { MaxArticlesPerFeed = 100 });

        await context.Service.CheckFeedAsync(new FeedConfig
        {
            Id = "feed_regular",
            Name = "Regular",
            Url = "https://example.test/rss"
        });

        Assert.Single(handler.Requests);
        Assert.Equal("/rss", handler.Requests[0].AbsolutePath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }

    private TestContext CreateContext(RoutingHandler handler, AppSettings settings)
    {
        var paths = new DataPaths(_tempDirectory);
        var configStore = new JsonConfigStore();
        new DataInitializer(paths, configStore).Initialize();
        var ruleMatchService = new RuleMatchService();
        var currentFilter = new CurrentFilterService(
            settings,
            configStore,
            paths.SettingsFile,
            ruleMatchService,
            []);
        var stateStore = new FeedStateStore(configStore, paths.FeedStateFile);
        var httpClient = new HttpClient(handler);
        var fetchService = new RssFetchService(httpClient);
        var historyService = new MikanHistoryService(fetchService, (_, _) => Task.CompletedTask);
        var service = new FeedCheckService(
            fetchService,
            new RssParseService(),
            new MagnetExtractService(),
            new JsonlItemStore(paths.ItemCacheFile),
            stateStore,
            new Logger(paths.AppLogFile, paths.ErrorLogFile),
            settings,
            ruleMatchService,
            currentFilter,
            new FeedDiagnosticsService(),
            historyService);
        return new TestContext(service, stateStore, httpClient);
    }

    private static FeedConfig CreateMikanFeed()
    {
        return new FeedConfig
        {
            Id = "feed_mikan",
            Name = "Mikan",
            Url = "https://mikanani.me/RSS/Classic"
        };
    }

    private static string Rss(string hash)
    {
        return $"""
            <rss><channel><item><title>Example CHS 1080p</title>
            <description>magnet:?xt=urn:btih:{hash}</description></item></channel></rss>
            """;
    }

    private static string HtmlPage(params string[] rows)
    {
        return $"<html><table><tbody>{string.Join(string.Empty, rows)}</tbody></table></html>";
    }

    private static string HtmlRow(string hash)
    {
        return $"""
            <tr><td>今天 12:00</td><td>Group</td>
            <td><a href="/Home/Episode/{hash}" class="magnet-link-wrap">Example CHS 1080p {hash}</a>
            <a data-clipboard-text="magnet:?xt=urn:btih:{hash}" class="js-magnet magnet-link">copy</a></td>
            <td>1 MB</td><td><a href="/Download/20260602/{hash}.torrent">download</a></td></tr>
            """;
    }

    private sealed record TestContext(
        FeedCheckService Service,
        FeedStateStore StateStore,
        HttpClient HttpClient) : IDisposable
    {
        public void Dispose()
        {
            HttpClient.Dispose();
        }
    }

    private sealed class RoutingHandler : HttpMessageHandler
    {
        private readonly Func<Uri, string> _response;

        public RoutingHandler(Func<Uri, string> response)
        {
            _response = response;
        }

        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_response(request.RequestUri!), Encoding.UTF8, "text/html")
            });
        }
    }
}
