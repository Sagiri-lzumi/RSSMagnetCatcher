using System.Net;
using System.Text;
using RSSMagnetCatcher.Core.Models;
using RSSMagnetCatcher.Core.Services;

namespace RSSMagnetCatcher.Tests;

public sealed class MikanHistoryServiceTests
{
    [Fact]
    public void ParsePage_DecodesTitleMagnetAndTorrentUrl()
    {
        using var httpClient = new HttpClient(new RoutingHandler(_ => string.Empty));
        var service = CreateService(httpClient);

        var item = Assert.Single(service.ParsePage(HtmlPage(HtmlRow("aaa111", "今天 12:00", "A&amp;B 1080p")), ClassicUrl));

        Assert.Equal("/Home/Episode/aaa111", item.SourceKey);
        Assert.Equal("A&B 1080p", item.Title);
        Assert.Equal("https://mikanani.me/Download/20260602/aaa111.torrent", item.CandidateTexts[2]);
        Assert.Contains("&tr=http%3a%2f%2ftracker", item.CandidateTexts[1]);
        Assert.NotNull(item.PublishedAt);
    }

    [Fact]
    public async Task FetchAsync_StopsWhenTargetIsReached()
    {
        var handler = new RoutingHandler(_ => HtmlPage(
            HtmlRow("aaa111", "今天 12:00", "One"),
            HtmlRow("bbb222", "今天 11:00", "Two")));
        using var httpClient = new HttpClient(handler);
        var service = CreateService(httpClient);

        var result = await service.FetchAsync(CreateFeed(), 2, 1);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(1, result.PagesFetched);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task FetchAsync_StopsWhenNextPageOnlyContainsDuplicates()
    {
        using var httpClient = new HttpClient(new RoutingHandler(_ => HtmlPage(HtmlRow("aaa111", "今天 12:00", "One"))));
        var service = CreateService(httpClient);

        var result = await service.FetchAsync(CreateFeed(), 3, 1);

        Assert.Single(result.Items);
        Assert.Equal(2, result.PagesFetched);
    }

    [Fact]
    public async Task FetchAsync_ReturnsWarningInsteadOfThrowingWhenPageFails()
    {
        using var httpClient = new HttpClient(new RoutingHandler(_ => throw new HttpRequestException("offline")));
        var service = CreateService(httpClient);

        var result = await service.FetchAsync(CreateFeed(), 3, 1);

        Assert.False(result.CompletedTarget);
        Assert.Contains("offline", result.Warning);
    }

    private const string ClassicUrl = "https://mikanani.me/RSS/Classic";

    private static MikanHistoryService CreateService(HttpClient client)
    {
        return new MikanHistoryService(new RssFetchService(client), (_, _) => Task.CompletedTask);
    }

    private static FeedConfig CreateFeed()
    {
        return new FeedConfig { Url = ClassicUrl, EnableMikanHistoryBackfill = true };
    }

    private static string HtmlPage(params string[] rows)
    {
        return $"<html><table><tbody>{string.Join(string.Empty, rows)}</tbody></table></html>";
    }

    private static string HtmlRow(string hash, string published, string title)
    {
        return $"""
            <tr>
              <td>{published}</td>
              <td>Group</td>
              <td><a href="/Home/Episode/{hash}" class="magnet-link-wrap">{title}</a>
                <a data-clipboard-text="magnet:?xt=urn:btih:{hash}&amp;tr=http%3a%2f%2ftracker" class="js-magnet magnet-link">copy</a></td>
              <td>1 MB</td>
              <td><a href="/Download/20260602/{hash}.torrent">download</a></td>
            </tr>
            """;
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
