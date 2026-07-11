using RSSMagnetCatcher.Core.Models;

namespace RSSMagnetCatcher.Core.Services;

public sealed class ClipboardExportService
{
    public IReadOnlyList<MagnetItem> SelectUnexported(IEnumerable<MagnetItem> items)
    {
        return items
            .Where(item => !string.IsNullOrWhiteSpace(item.Magnet))
            .Where(item => !item.IsExported)
            .Where(item => string.Equals(item.ProcessingStatus, ProcessingStatuses.Pending, StringComparison.Ordinal))
            .ToList();
    }

    public IReadOnlyList<MagnetItem> SelectMatching(
        IEnumerable<MagnetItem> items,
        Func<MagnetItem, bool> isMatched,
        bool requireUnexported)
    {
        return items
            .Where(item => !string.IsNullOrWhiteSpace(item.Magnet))
            .Where(item => !string.Equals(item.ProcessingStatus, ProcessingStatuses.Deleted, StringComparison.Ordinal))
            .Where(item => !requireUnexported
                || (!item.IsExported
                    && string.Equals(item.ProcessingStatus, ProcessingStatuses.Pending, StringComparison.Ordinal)))
            .Where(isMatched)
            .ToList();
    }

    public string BuildClipboardText(IEnumerable<MagnetItem> items)
    {
        return string.Join("\r\n", BuildClipboardMagnets(items));
    }

    public IReadOnlyList<string> BuildClipboardMagnets(IEnumerable<MagnetItem> items)
    {
        return items
            .Where(item => !string.IsNullOrWhiteSpace(item.Magnet))
            .OrderBy(item => item.FoundAt)
            .GroupBy(
                item => string.IsNullOrWhiteSpace(item.InfoHash) ? item.Magnet : item.InfoHash,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First().Magnet)
            .ToList();
    }

    public int CopyToClipboard(IEnumerable<MagnetItem> items)
    {
        var magnets = BuildClipboardMagnets(items);
        var text = string.Join("\r\n", magnets);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("没有可复制的 magnet。");
        }

        Clipboard.SetText(text);
        return magnets.Count;
    }
}
