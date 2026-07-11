using RSSMagnetCatcher.Core.Services;

namespace RSSMagnetCatcher.Tests;

public sealed class MagnetExtractServiceTests
{
    private readonly MagnetExtractService _service = new();

    [Fact]
    public void Extract_DecodesCandidatesAndDeduplicatesInfoHashes()
    {
        var result = _service.Extract(
        [
            "magnet:?xt=urn:btih:ABC123&amp;dn=First",
            "MAGNET:?xt=urn:btih:abc123&dn=Duplicate",
            "magnet%3A%3Fxt%3Durn%3Abtih%3ADEF456%26dn%3DSecond"
        ]);

        Assert.Equal(2, result.Magnets.Count);
        Assert.Contains(result.Magnets, magnet => magnet.InfoHash == "abc123" && magnet.Magnet.Contains("&dn=First"));
        Assert.Contains(result.Magnets, magnet => magnet.InfoHash == "def456");
    }

    [Fact]
    public void Extract_ReturnsTorrentUrlWhenNoMagnetExists()
    {
        var result = _service.Extract(["下载：https://example.test/files/demo.torrent?token=1"]);

        Assert.Empty(result.Magnets);
        Assert.Equal("https://example.test/files/demo.torrent?token=1", Assert.Single(result.TorrentUrls));
    }

    [Fact]
    public void Extract_InfersMagnetFromHexTorrentFileName()
    {
        const string hash = "3b1b057bc76a806ca14108ce0a2cbb378a900f32";

        var result = _service.Extract([$"https://mikanani.me/Download/20260602/{hash}.torrent"]);

        var magnet = Assert.Single(result.Magnets);
        Assert.Equal(hash, magnet.InfoHash);
        Assert.Equal($"magnet:?xt=urn:btih:{hash}", magnet.Magnet);
        Assert.Equal($"https://mikanani.me/Download/20260602/{hash}.torrent", magnet.SourceTorrentUrl);
    }

    [Fact]
    public void Extract_InfersMagnetFromBase32TorrentFileName()
    {
        const string hash = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        var result = _service.Extract([$"https://example.test/files/{hash}.torrent?token=1"]);

        var magnet = Assert.Single(result.Magnets);
        Assert.Equal(hash.ToLowerInvariant(), magnet.InfoHash);
    }

    [Fact]
    public void Extract_PrefersExplicitMagnetWhenTorrentUrlHasSameHash()
    {
        const string hash = "3b1b057bc76a806ca14108ce0a2cbb378a900f32";

        var result = _service.Extract(
        [
            $"https://example.test/files/{hash}.torrent",
            $"magnet:?xt=urn:btih:{hash}&dn=Explicit"
        ]);

        Assert.Equal($"magnet:?xt=urn:btih:{hash}&dn=Explicit", Assert.Single(result.Magnets).Magnet);
    }
}
