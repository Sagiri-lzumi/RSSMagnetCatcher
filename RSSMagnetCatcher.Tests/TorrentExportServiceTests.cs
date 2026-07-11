using System.Net;
using System.Text;
using RSSMagnetCatcher.Core.Models;
using RSSMagnetCatcher.Core.Services;
using RSSMagnetCatcher.Storage;

namespace RSSMagnetCatcher.Tests;

public sealed class TorrentExportServiceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "RRSMC.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ExportAsync_SavesAllTorrentsIntoTimestampFolder()
    {
        var service = CreateService(_ => Ok("torrent bytes"));

        var result = await service.ExportAsync(
        [
            CreateItem("one", "https://example.test/files/one.torrent"),
            CreateItem("two", "https://example.test/files/two.torrent")
        ]);

        Assert.Equal(2, result.SuccessCount);
        Assert.EndsWith(Path.Combine("data", "torrent_exports", "20260603153045"), result.ExportDirectory);
        Assert.True(File.Exists(Path.Combine(result.ExportDirectory, "one.torrent")));
        Assert.True(File.Exists(Path.Combine(result.ExportDirectory, "two.torrent")));
    }

    [Fact]
    public async Task ExportAsync_DeduplicatesFileNamesAndUsesFallbackName()
    {
        var service = CreateService(_ => Ok("torrent bytes"));

        var result = await service.ExportAsync(
        [
            CreateItem("same1", "https://example.test/files/same.torrent"),
            CreateItem("same2", "https://example.test/files/same.torrent?token=2"),
            CreateItem("fallback", "https://example.test/download", "A:B/C")
        ]);

        Assert.Equal(3, result.SuccessCount);
        Assert.True(File.Exists(Path.Combine(result.ExportDirectory, "same.torrent")));
        Assert.True(File.Exists(Path.Combine(result.ExportDirectory, "same_2.torrent")));
        Assert.Contains(
            Directory.GetFiles(result.ExportDirectory).Select(Path.GetFileName),
            name => name is not null && name.StartsWith("A_B_C_", StringComparison.Ordinal) && name.EndsWith(".torrent"));
    }

    [Fact]
    public async Task ExportAsync_PartialFailuresAreNotSuccessful()
    {
        var service = CreateService(request =>
            request.RequestUri!.AbsolutePath.Contains("fail", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : Ok("torrent bytes"));

        var result = await service.ExportAsync(
        [
            CreateItem("ok", "https://example.test/files/ok.torrent"),
            CreateItem("fail", "https://example.test/files/fail.torrent")
        ]);

        Assert.Equal(1, result.SuccessCount);
        Assert.Equal(1, result.FailureCount);
        Assert.Equal(["ok"], result.SuccessfulItems.Select(item => item.Id));
    }

    [Fact]
    public async Task ExportAsync_SkipsItemsWithoutTorrentUrl()
    {
        var service = CreateService(_ => Ok("torrent bytes"));

        var result = await service.ExportAsync([CreateItem("missing", string.Empty)]);

        Assert.Equal(0, result.SuccessCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Equal(string.Empty, result.ExportDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }

    private TorrentExportService CreateService(Func<HttpRequestMessage, HttpResponseMessage> response)
    {
        var paths = new DataPaths(_tempDirectory);
        var client = new HttpClient(new RoutingHandler(response));
        return new TorrentExportService(
            paths,
            client,
            () => DateTimeOffset.Parse("2026-06-03T15:30:45+08:00"));
    }

    private static HttpResponseMessage Ok(string content)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(content))
        };
    }

    private static MagnetItem CreateItem(string id, string torrentUrl, string title = "Example")
    {
        return new MagnetItem
        {
            Id = id,
            FeedId = "feed_a",
            Title = title,
            TorrentUrl = torrentUrl,
            Magnet = $"magnet:?xt=urn:btih:{id}",
            InfoHash = id,
            FoundAt = DateTimeOffset.Parse("2026-06-03T10:00:00+08:00"),
            ProcessingStatus = ProcessingStatuses.Pending
        };
    }

    private sealed class RoutingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _response;

        public RoutingHandler(Func<HttpRequestMessage, HttpResponseMessage> response)
        {
            _response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_response(request));
        }
    }
}
