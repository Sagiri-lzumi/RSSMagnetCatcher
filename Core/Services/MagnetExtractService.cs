using System.Net;
using System.Text.RegularExpressions;

namespace RSSMagnetCatcher.Core.Services;

public sealed record ExtractedMagnet(string Magnet, string InfoHash, string SourceTorrentUrl = "");

public sealed record MagnetExtractionResult(
    IReadOnlyList<ExtractedMagnet> Magnets,
    IReadOnlyList<string> TorrentUrls);

public sealed partial class MagnetExtractService
{
    private static readonly char[] TrailingPunctuation =
    [
        '.', ',', ';', ':', '!', '?', ')', ']', '}', '\'', '"',
        '。', '，', '；', '：', '！', '？', '）', '】', '》'
    ];

    public MagnetExtractionResult Extract(IEnumerable<string> candidateTexts)
    {
        var magnets = new Dictionary<string, ExtractedMagnet>(StringComparer.OrdinalIgnoreCase);
        var inferredMagnets = new Dictionary<string, ExtractedMagnet>(StringComparer.OrdinalIgnoreCase);
        var torrentUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidateTexts.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            foreach (var decoded in DecodeVariants(candidate))
            {
                foreach (Match match in MagnetRegex().Matches(decoded))
                {
                    var magnet = Clean(WebUtility.HtmlDecode(match.Value));
                    var hashMatch = InfoHashRegex().Match(magnet);
                    if (!hashMatch.Success)
                    {
                        continue;
                    }

                    var infoHash = hashMatch.Groups["hash"].Value.ToLowerInvariant();
                    magnets.TryAdd(infoHash, new ExtractedMagnet(magnet, infoHash));
                }

                foreach (Match match in TorrentRegex().Matches(decoded))
                {
                    var torrentUrl = Clean(WebUtility.HtmlDecode(match.Value));
                    torrentUrls.Add(torrentUrl);
                    if (TryCreateMagnetFromTorrentUrl(torrentUrl, out var inferredMagnet))
                    {
                        inferredMagnets.TryAdd(inferredMagnet.InfoHash, inferredMagnet);
                    }
                }
            }
        }

        var extractedMagnets = magnets.Values
            .Concat(inferredMagnets
                .Where(pair => !magnets.ContainsKey(pair.Key))
                .Select(pair => pair.Value))
            .ToList();
        return new MagnetExtractionResult(extractedMagnets, torrentUrls.ToList());
    }

    public bool TryCreateMagnetFromTorrentUrl(string torrentUrl, out ExtractedMagnet magnet)
    {
        magnet = new ExtractedMagnet(string.Empty, string.Empty);
        if (!Uri.TryCreate(torrentUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var fileName = Uri.UnescapeDataString(Path.GetFileName(uri.AbsolutePath));
        if (!fileName.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var infoHash = Path.GetFileNameWithoutExtension(fileName);
        if (!HexInfoHashRegex().IsMatch(infoHash) && !Base32InfoHashRegex().IsMatch(infoHash))
        {
            return false;
        }

        infoHash = infoHash.ToLowerInvariant();
        magnet = new ExtractedMagnet($"magnet:?xt=urn:btih:{infoHash}", infoHash, torrentUrl);
        return true;
    }

    private static IReadOnlyCollection<string> DecodeVariants(string value)
    {
        var variants = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>();
        AddDecoded(variants, pending, value);

        while (pending.Count > 0 && variants.Count < 16)
        {
            var candidate = pending.Dequeue();
            AddDecoded(variants, pending, WebUtility.HtmlDecode(candidate));

            try
            {
                AddDecoded(variants, pending, Uri.UnescapeDataString(candidate));
            }
            catch (UriFormatException)
            {
                // Keep scanning the original text when malformed escaping appears in a feed.
            }
        }

        return variants;
    }

    private static void AddDecoded(ISet<string> variants, Queue<string> pending, string? decoded)
    {
        if (!string.IsNullOrWhiteSpace(decoded) && variants.Add(decoded))
        {
            pending.Enqueue(decoded);
        }
    }

    private static string Clean(string value)
    {
        return value.Trim().TrimEnd(TrailingPunctuation);
    }

    [GeneratedRegex(@"magnet:\?xt=urn:btih:[A-Za-z0-9]+[^\s<>'""]*", RegexOptions.IgnoreCase)]
    private static partial Regex MagnetRegex();

    [GeneratedRegex(@"xt=urn:btih:(?<hash>[A-Za-z0-9]+)", RegexOptions.IgnoreCase)]
    private static partial Regex InfoHashRegex();

    [GeneratedRegex(@"https?://[^\s<>'""]+?\.torrent(?:\?[^\s<>'""]*)?", RegexOptions.IgnoreCase)]
    private static partial Regex TorrentRegex();

    [GeneratedRegex(@"^[A-Fa-f0-9]{40}$")]
    private static partial Regex HexInfoHashRegex();

    [GeneratedRegex(@"^[A-Za-z2-7]{32}$")]
    private static partial Regex Base32InfoHashRegex();
}
