using RSSMagnetCatcher.Core.Models;
using RSSMagnetCatcher.Storage;

namespace RSSMagnetCatcher.Core.Services;

public sealed record CacheMaintenanceResult(int RemovedItems, int RemovedHistoryEntries);

public sealed class CacheMaintenanceService
{
    private readonly JsonlItemStore _itemStore;
    private readonly ExportHistoryStore _historyStore;
    private readonly ActiveBatchStore _batchStore;
    private readonly AppSettings _settings;

    public CacheMaintenanceService(
        JsonlItemStore itemStore,
        ExportHistoryStore historyStore,
        ActiveBatchStore batchStore,
        AppSettings settings)
    {
        _itemStore = itemStore;
        _historyStore = historyStore;
        _batchStore = batchStore;
        _settings = settings;
    }

    public CacheMaintenanceResult Compact(DateTimeOffset? now = null)
    {
        var cutoff = (now ?? DateTimeOffset.Now).AddDays(-Math.Max(0, _settings.KeepHistoryDays));
        var items = _itemStore.LoadLatest();
        var history = _historyStore.Load();
        var exportedAtByItemId = history
            .GroupBy(entry => entry.ItemId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Max(entry => entry.ExportedAt),
                StringComparer.Ordinal);

        var activeBatch = _batchStore.Load();
        var activeBatchItemIds = activeBatch.IsActive
            ? activeBatch.ItemIds.ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        var protectedItems = items
            .Where(item => IsProtected(item, activeBatchItemIds))
            .OrderByDescending(item => item.FoundAt)
            .ToList();
        var processedItems = items
            .Where(item => !IsProtected(item, activeBatchItemIds))
            .Where(item => GetRetentionDate(item, exportedAtByItemId) >= cutoff)
            .OrderByDescending(item => GetRetentionDate(item, exportedAtByItemId))
            .ToList();
        var retainedProcessed = LimitProcessedPerFeed(processedItems, protectedItems);
        var processedSlots = Math.Max(0, Math.Max(1, _settings.MaxCacheItems) - protectedItems.Count);
        var retainedItems = protectedItems
            .Concat(retainedProcessed.Take(processedSlots))
            .OrderBy(item => item.FoundAt)
            .ToList();
        var retainedIds = retainedItems.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var retainedHistory = history
            .Where(entry => entry.ExportedAt >= cutoff && retainedIds.Contains(entry.ItemId))
            .OrderBy(entry => entry.ExportedAt)
            .ToList();

        _itemStore.Rewrite(retainedItems);
        _historyStore.Rewrite(retainedHistory);
        return new CacheMaintenanceResult(items.Count - retainedItems.Count, history.Count - retainedHistory.Count);
    }

    public CacheMaintenanceResult CleanExportedHistory()
    {
        var items = _itemStore.LoadLatest();
        var retainedItems = items
            .Where(item => !string.Equals(item.ProcessingStatus, ProcessingStatuses.Used, StringComparison.Ordinal))
            .OrderBy(item => item.FoundAt)
            .ToList();
        var historyCount = _historyStore.Load().Count;
        _itemStore.Rewrite(retainedItems);
        _historyStore.Clear();
        return new CacheMaintenanceResult(items.Count - retainedItems.Count, historyCount);
    }

    private static DateTimeOffset GetRetentionDate(
        MagnetItem item,
        IReadOnlyDictionary<string, DateTimeOffset> exportedAtByItemId)
    {
        return exportedAtByItemId.GetValueOrDefault(item.Id, item.FoundAt);
    }

    private static bool IsProtected(MagnetItem item, IReadOnlySet<string> activeBatchItemIds)
    {
        return activeBatchItemIds.Contains(item.Id)
            || ((!string.IsNullOrWhiteSpace(item.Magnet)
                    || !string.IsNullOrWhiteSpace(item.TorrentUrl))
                && string.Equals(item.ProcessingStatus, ProcessingStatuses.Pending, StringComparison.Ordinal));
    }

    private IReadOnlyList<MagnetItem> LimitProcessedPerFeed(
        IEnumerable<MagnetItem> processed,
        IEnumerable<MagnetItem> protectedItems)
    {
        var maximum = Math.Clamp(_settings.MaxArticlesPerFeed, 100, 10000);
        var retainedCounts = protectedItems
            .GroupBy(item => item.FeedId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var retained = new List<MagnetItem>();
        foreach (var item in processed)
        {
            var count = retainedCounts.GetValueOrDefault(item.FeedId);
            if (count >= maximum)
            {
                continue;
            }

            retained.Add(item);
            retainedCounts[item.FeedId] = count + 1;
        }

        return retained;
    }
}
