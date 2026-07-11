using RSSMagnetCatcher.Core.Services;

namespace RSSMagnetCatcher.Tests;

public sealed class RssParseServiceTests
{
    private readonly RssParseService _parseService = new();
    private readonly MagnetExtractService _extractService = new();

    [Fact]
    public void Parse_RssScansDescriptionEnclosureAndUnknownNamespaceFields()
    {
        const string xml = """
            <rss xmlns:custom="urn:test">
              <channel>
                <item>
                  <title>Example 1080p CHS</title>
                  <guid>item-1</guid>
                  <description><![CDATA[<a href="magnet:?xt=urn:btih:AAA111&amp;dn=Example">download</a>]]></description>
                  <enclosure url="https://example.test/files/example.torrent" />
                  <custom:extra>magnet%3A%3Fxt%3Durn%3Abtih%3ABBB222</custom:extra>
                </item>
              </channel>
            </rss>
            """;

        var rssItem = Assert.Single(_parseService.Parse(xml));
        var extraction = _extractService.Extract(rssItem.CandidateTexts);

        Assert.Equal("Example 1080p CHS", rssItem.Title);
        Assert.Equal(2, extraction.Magnets.Count);
        Assert.Equal("https://example.test/files/example.torrent", Assert.Single(extraction.TorrentUrls));
    }

    [Fact]
    public void Parse_AtomScansLinkHref()
    {
        const string xml = """
            <feed xmlns="http://www.w3.org/2005/Atom">
              <entry>
                <title>Atom item</title>
                <id>atom-1</id>
                <link href="magnet:?xt=urn:btih:ATOM123&amp;dn=Atom" />
                <updated>2026-06-01T10:00:00+08:00</updated>
              </entry>
            </feed>
            """;

        var rssItem = Assert.Single(_parseService.Parse(xml));
        var magnet = Assert.Single(_extractService.Extract(rssItem.CandidateTexts).Magnets);

        Assert.Equal("atom-1", rssItem.SourceKey);
        Assert.Equal("atom123", magnet.InfoHash);
    }

    [Fact]
    public void Parse_MikanStyleEnclosureInfersMagnetWithoutDownloadingTorrent()
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

        var rssItem = Assert.Single(_parseService.Parse(xml));
        var extraction = _extractService.Extract(rssItem.CandidateTexts);

        Assert.Equal(hash, Assert.Single(extraction.Magnets).InfoHash);
    }
}
