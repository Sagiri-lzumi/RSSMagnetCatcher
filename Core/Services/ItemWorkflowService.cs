using RSSMagnetCatcher.Core.Models;
using RSSMagnetCatcher.Storage;

namespace RSSMagnetCatcher.Core.Services;

public sealed record BatchStartResult(bool Started, int CandidateCount, int CheckedCount, string Message);

public sealed record BatchCompletionResult(int UsedCount, int DiscardedCount);

public sealed class ItemWorkflowService
{
    private readonly JsonlItemStore _itemStore;
    private readonly ActiveBatchStore _batchStore;
    private readonly ItemFilterService _itemFilterService;

    public ItemWorkflowService(
        JsonlItemStore itemStore,
        ActiveBatchStore batchStore,
        ItemFilterService itemFilterService)
    {
        _itemStore = itemStore;
        _batchStore = batchStore;
        _itemFilterService = itemFilterService;
    }

    public ActiveBatch ActiveBatch => _batchStore.Load();

    public int NormalizeCache()
    {
        var changed = 0;
        foreach (var item in _itemStore.LoadLatest())
        {
            if (NormalizeItem(item))
            {
                _itemStore.Append(item);
                changed++;
            }
        }

        return changed;
    }

    public bool IsCopyable(MagnetItem item)
    {
        return !string.IsNullOrWhiteSpace(item.Magnet)
            && !string.Equals(item.ProcessingStatus, ProcessingStatuses.Deleted, StringComparison.Ordinal);
    }

    public bool IsTorrentExportable(MagnetItem item)
    {
        return !string.IsNullOrWhiteSpace(item.TorrentUrl)
            && !string.Equals(item.ProcessingStatus, ProcessingStatuses.Deleted, StringComparison.Ordinal);
    }

    public bool IsExportable(MagnetItem item)
    {
        return (!string.IsNullOrWhiteSpace(item.Magnet)
                || !string.IsNullOrWhiteSpace(item.TorrentUrl))
            && !string.Equals(item.ProcessingStatus, ProcessingStatuses.Deleted, StringComparison.Ordinal);
    }

    public bool IsPending(MagnetItem item)
    {
        return IsExportable(item)
            && string.Equals(item.ProcessingStatus, ProcessingStatuses.Pending, StringComparison.Ordinal);
    }

    public bool IsDiscarded(MagnetItem item)
    {
        return IsExportable(item)
            && string.Equals(item.ProcessingStatus, ProcessingStatuses.Discarded, StringComparison.Ordinal);
    }

    public bool IsUsed(MagnetItem item)
    {
        return IsExportable(item)
            && string.Equals(item.ProcessingStatus, ProcessingStatuses.Used, StringComparison.Ordinal);
    }

    public BatchStartResult StartBatch(
        IEnumerable<MagnetItem> items,
        ItemListViewMode sourceMode,
        string? feedId,
        bool useCurrentRule)
    {
        var existingBatch = _batchStore.Load();
        if (existingBatch.IsActive)
        {
            return new BatchStartResult(false, 0, 0, "已有待结算批次，请先复制磁力、导出种子或取消本次批选。");
        }

        var sourceStatus = sourceMode == ItemListViewMode.Discarded
            ? ProcessingStatuses.Discarded
            : ProcessingStatuses.Pending;
        var candidates = items
            .Where(IsExportable)
            .Where(item => string.Equals(item.ProcessingStatus, sourceStatus, StringComparison.Ordinal))
            .ToList();
        if (candidates.Count == 0)
        {
            return new BatchStartResult(false, 0, 0, "当前列表没有可批选的导出项。");
        }

        var checkedCount = 0;
        var originalCheckedById = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var item in candidates)
        {
            originalCheckedById[item.Id] = item.IsChecked;
            var shouldCheck = !useCurrentRule || _itemFilterService.IsMatched(item);
            if (shouldCheck)
            {
                checkedCount++;
            }

            if (item.IsChecked != shouldCheck)
            {
                item.IsChecked = shouldCheck;
                _itemStore.Append(item);
            }
        }

        _batchStore.Save(new ActiveBatch
        {
            IsActive = true,
            Id = $"batch_{Guid.NewGuid():N}",
            SourceMode = sourceMode.ToString(),
            FeedId = feedId,
            SourceProcessingStatus = sourceStatus,
            SelectionMode = useCurrentRule ? BatchSelectionModes.RuleMatched : BatchSelectionModes.All,
            CreatedAt = DateTimeOffset.Now,
            ItemIds = candidates.Select(item => item.Id).ToList(),
            OriginalCheckedByItemId = originalCheckedById
        });

        return new BatchStartResult(
            true,
            candidates.Count,
            checkedCount,
            useCurrentRule
                ? $"已按条件勾选 {checkedCount} / {candidates.Count} 条，可手动增减后复制或导出。"
                : $"已勾选当前范围 {checkedCount} 条，可手动增减后复制或导出。");
    }

    public int ClearChecked(IEnumerable<MagnetItem> items)
    {
        var changed = 0;
        foreach (var item in items.Where(IsExportable))
        {
            if (!item.IsChecked)
            {
                continue;
            }

            item.IsChecked = false;
            _itemStore.Append(item);
            changed++;
        }

        return changed;
    }

    public int CancelActiveBatch()
    {
        var batch = _batchStore.Load();
        if (!batch.IsActive)
        {
            return 0;
        }

        var latestById = _itemStore.LoadLatest().ToDictionary(item => item.Id, StringComparer.Ordinal);
        var changed = 0;
        foreach (var pair in batch.OriginalCheckedByItemId)
        {
            if (!latestById.TryGetValue(pair.Key, out var item) || item.IsChecked == pair.Value)
            {
                continue;
            }

            item.IsChecked = pair.Value;
            _itemStore.Append(item);
            changed++;
        }

        _batchStore.Clear();
        return changed;
    }

    public BatchCompletionResult CompleteCopy(IEnumerable<MagnetItem> copiedItems)
    {
        return CompleteExport(copiedItems);
    }

    public BatchCompletionResult CompleteExport(
        IEnumerable<MagnetItem> exportedItems,
        IEnumerable<string>? keepUnsettledItemIds = null)
    {
        var exportedIds = exportedItems
            .Where(IsExportable)
            .Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);
        if (exportedIds.Count == 0)
        {
            return new BatchCompletionResult(0, 0);
        }

        var keepUnsettledIds = (keepUnsettledItemIds ?? [])
            .ToHashSet(StringComparer.Ordinal);
        var batch = _batchStore.Load();
        var latestById = _itemStore.LoadLatest().ToDictionary(item => item.Id, StringComparer.Ordinal);
        var usedCount = 0;
        var discardedCount = 0;

        foreach (var exportedId in exportedIds)
        {
            if (!latestById.TryGetValue(exportedId, out var item) || !IsExportable(item))
            {
                continue;
            }

            var wasUsed = string.Equals(item.ProcessingStatus, ProcessingStatuses.Used, StringComparison.Ordinal);
            item.ProcessingStatus = ProcessingStatuses.Used;
            item.IsExported = true;
            item.IsChecked = false;
            if (string.Equals(item.MatchStatus, MatchStatuses.Exported, StringComparison.Ordinal))
            {
                item.MatchStatus = MatchStatuses.Extracted;
            }

            _itemStore.Append(item);
            if (!wasUsed)
            {
                usedCount++;
            }
        }

        if (batch.IsActive)
        {
            foreach (var itemId in batch.ItemIds.Where(itemId => !exportedIds.Contains(itemId)))
            {
                if (keepUnsettledIds.Contains(itemId)
                    || !latestById.TryGetValue(itemId, out var item)
                    || !IsExportable(item))
                {
                    continue;
                }

                if (string.Equals(batch.SourceProcessingStatus, ProcessingStatuses.Pending, StringComparison.Ordinal)
                    && string.Equals(item.ProcessingStatus, ProcessingStatuses.Pending, StringComparison.Ordinal))
                {
                    item.ProcessingStatus = ProcessingStatuses.Discarded;
                    item.IsExported = false;
                    item.IsChecked = false;
                    _itemStore.Append(item);
                    discardedCount++;
                }
                else if (string.Equals(batch.SourceProcessingStatus, ProcessingStatuses.Discarded, StringComparison.Ordinal)
                    && string.Equals(item.ProcessingStatus, ProcessingStatuses.Discarded, StringComparison.Ordinal)
                    && item.IsChecked)
                {
                    item.IsChecked = false;
                    _itemStore.Append(item);
                }
            }

            _batchStore.Clear();
        }

        return new BatchCompletionResult(usedCount, discardedCount);
    }

    public int RestoreDiscarded(IEnumerable<MagnetItem> items)
    {
        var changed = 0;
        foreach (var item in items.Where(IsDiscarded))
        {
            item.ProcessingStatus = ProcessingStatuses.Pending;
            item.IsExported = false;
            item.IsChecked = false;
            _itemStore.Append(item);
            changed++;
        }

        return changed;
    }

    public int SoftDelete(IEnumerable<MagnetItem> items)
    {
        var changed = 0;
        foreach (var item in items.Where(item =>
            !string.Equals(item.ProcessingStatus, ProcessingStatuses.Deleted, StringComparison.Ordinal)))
        {
            item.ProcessingStatus = ProcessingStatuses.Deleted;
            item.IsExported = false;
            item.IsChecked = false;
            _itemStore.Append(item);
            changed++;
        }

        return changed;
    }

    public bool ApplyManualChecked(MagnetItem item)
    {
        if (IsExportable(item))
        {
            _itemStore.Append(item);
            return true;
        }

        if (item.IsChecked)
        {
            item.IsChecked = false;
            _itemStore.Append(item);
            return false;
        }

        _itemStore.Append(item);
        return false;
    }

    private static bool NormalizeItem(MagnetItem item)
    {
        var changed = false;
        var status = NormalizeProcessingStatus(item.ProcessingStatus);
        if (!string.Equals(item.ProcessingStatus, status, StringComparison.Ordinal))
        {
            item.ProcessingStatus = status;
            changed = true;
        }

        if (item.IsExported || string.Equals(item.MatchStatus, MatchStatuses.Exported, StringComparison.Ordinal))
        {
            if (!string.Equals(item.ProcessingStatus, ProcessingStatuses.Used, StringComparison.Ordinal))
            {
                item.ProcessingStatus = ProcessingStatuses.Used;
                changed = true;
            }
        }

        var shouldBeExported = string.Equals(item.ProcessingStatus, ProcessingStatuses.Used, StringComparison.Ordinal);
        if (item.IsExported != shouldBeExported)
        {
            item.IsExported = shouldBeExported;
            changed = true;
        }

        if (string.Equals(item.MatchStatus, MatchStatuses.Exported, StringComparison.Ordinal))
        {
            item.MatchStatus = string.IsNullOrWhiteSpace(item.Magnet)
                ? string.IsNullOrWhiteSpace(item.TorrentUrl)
                    ? MatchStatuses.NoMagnet
                    : MatchStatuses.TorrentOnly
                : MatchStatuses.Extracted;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(item.MatchStatus))
        {
            item.MatchStatus = string.IsNullOrWhiteSpace(item.Magnet)
                ? string.IsNullOrWhiteSpace(item.TorrentUrl)
                    ? MatchStatuses.NoMagnet
                    : MatchStatuses.TorrentOnly
                : MatchStatuses.Extracted;
            changed = true;
        }

        return changed;
    }

    private static string NormalizeProcessingStatus(string? status)
    {
        return status switch
        {
            ProcessingStatuses.Pending => ProcessingStatuses.Pending,
            ProcessingStatuses.Discarded => ProcessingStatuses.Discarded,
            ProcessingStatuses.Used => ProcessingStatuses.Used,
            ProcessingStatuses.Deleted => ProcessingStatuses.Deleted,
            _ => ProcessingStatuses.Pending
        };
    }
}
