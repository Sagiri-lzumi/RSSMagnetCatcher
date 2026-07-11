using System.Net;
using System.Net.Sockets;
using System.Xml;
using RSSMagnetCatcher.Core.Models;
using RSSMagnetCatcher.Core.Services;

namespace RSSMagnetCatcher.Tests;

public sealed class FeedDiagnosticsServiceTests
{
    private readonly FeedDiagnosticsService _service = new();

    [Fact]
    public void ClassifyFailure_RecognizesHttpDnsAndXmlFailures()
    {
        Assert.Equal(
            FeedDiagnosticCategories.HttpError,
            _service.ClassifyFailure(new RssFetchException(HttpStatusCode.Forbidden, "forbidden")));
        Assert.Equal(
            FeedDiagnosticCategories.DnsFailure,
            _service.ClassifyFailure(new HttpRequestException("dns", new SocketException((int)SocketError.HostNotFound))));
        Assert.Equal(
            FeedDiagnosticCategories.XmlParseError,
            _service.ClassifyFailure(new XmlException("bad xml"), "<rss>"));
        Assert.Equal(
            FeedDiagnosticCategories.NonXml,
            _service.ClassifyFailure(new XmlException("bad xml"), "not xml"));
    }

    [Fact]
    public void BuildFailureReport_IncludesOnlyFailedFeeds()
    {
        var report = _service.BuildFailureReport(
        [
            new FeedConfig { Id = "failed", Name = "Failed", Url = "https://failed.test" },
            new FeedConfig { Id = "ok", Name = "OK", Url = "https://ok.test" }
        ],
        new Dictionary<string, FeedState>
        {
            ["failed"] = new()
            {
                LastStatus = "failed",
                LastErrorCategory = FeedDiagnosticCategories.DnsFailure,
                LastError = "host not found",
                ConsecutiveFailCount = 2
            },
            ["ok"] = new() { LastStatus = "ok" }
        });

        Assert.Contains("订阅：Failed", report);
        Assert.Contains("分类：DNS 失败", report);
        Assert.DoesNotContain("订阅：OK", report);
    }
}
