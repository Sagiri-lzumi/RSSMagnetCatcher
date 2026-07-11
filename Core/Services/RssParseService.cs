using System.Xml.Linq;
using RSSMagnetCatcher.Core.Models;

namespace RSSMagnetCatcher.Core.Services;

public sealed class NonRssXmlException : Exception
{
    public NonRssXmlException(string message)
        : base(message)
    {
    }
}

public sealed class RssParseService
{
    public IReadOnlyList<RssItem> Parse(string xml)
    {
        var document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        if (document.Root is null
            || !IsSupportedRoot(document.Root.Name.LocalName))
        {
            throw new NonRssXmlException("返回内容不是 RSS 或 Atom XML。");
        }

        return document
            .Descendants()
            .Where(element => IsNamed(element, "item") || IsNamed(element, "entry"))
            .Select(ParseEntry)
            .ToList();
    }

    private static RssItem ParseEntry(XElement entry)
    {
        var title = FindFirstElementValue(entry, "title");
        var sourceKey = FindFirstElementValue(entry, "guid", "id")
            ?? FindFirstLink(entry)
            ?? title
            ?? string.Empty;

        var candidates = entry
            .DescendantsAndSelf()
            .Select(element => element.Value)
            .Concat(entry.DescendantsAndSelf().Attributes().Select(attribute => attribute.Value))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return new RssItem
        {
            SourceKey = sourceKey.Trim(),
            Title = title?.Trim() ?? string.Empty,
            PublishedAt = ParsePublishedAt(entry),
            CandidateTexts = candidates
        };
    }

    private static DateTimeOffset? ParsePublishedAt(XElement entry)
    {
        var value = FindFirstElementValue(entry, "pubDate", "published", "updated");
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    private static string? FindFirstLink(XElement entry)
    {
        var link = entry.Elements().FirstOrDefault(element => IsNamed(element, "link"));
        return link?.Attribute("href")?.Value ?? link?.Value;
    }

    private static string? FindFirstElementValue(XElement entry, params string[] names)
    {
        return entry
            .Elements()
            .FirstOrDefault(element => names.Any(name => IsNamed(element, name)))
            ?.Value;
    }

    private static bool IsNamed(XElement element, string name)
    {
        return string.Equals(element.Name.LocalName, name, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSupportedRoot(string name)
    {
        return string.Equals(name, "rss", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "feed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "RDF", StringComparison.OrdinalIgnoreCase);
    }
}
