using RSSMagnetCatcher.Core.Models;
using RSSMagnetCatcher.Storage;

namespace RSSMagnetCatcher.Core.Services;

public sealed class ItemFilterService
{
    private readonly JsonlItemStore _itemStore;
    private readonly RuleMatchService _ruleMatchService;
    private readonly CurrentFilterService _currentFilterService;

    public ItemFilterService(
        JsonlItemStore itemStore,
        RuleMatchService ruleMatchService,
        CurrentFilterService currentFilterService)
    {
        _itemStore = itemStore;
        _ruleMatchService = ruleMatchService;
        _currentFilterService = currentFilterService;
    }

    public int ReevaluateAll()
    {
        var changedCount = 0;
        foreach (var item in _itemStore.LoadLatest())
        {
            if (string.IsNullOrWhiteSpace(item.Magnet)
                || string.Equals(item.ProcessingStatus, ProcessingStatuses.Used, StringComparison.Ordinal)
                || string.Equals(item.ProcessingStatus, ProcessingStatuses.Deleted, StringComparison.Ordinal))
            {
                continue;
            }

            var isMatched = IsMatched(item);
            var status = isMatched ? MatchStatuses.Extracted : MatchStatuses.Filtered;
            var wasChanged = !string.Equals(item.MatchStatus, status, StringComparison.Ordinal);

            if (wasChanged)
            {
                item.MatchStatus = status;
                _itemStore.Append(item);
                changedCount++;
            }
        }

        return changedCount;
    }

    public bool IsMatched(MagnetItem item)
    {
        var text = string.IsNullOrWhiteSpace(item.SearchText) ? item.Title : item.SearchText;
        return _ruleMatchService.IsMatch(_currentFilterService.CurrentExpression, text);
    }

    public bool IsEligibleForExport(MagnetItem item, bool requireUnexported)
    {
        return !string.IsNullOrWhiteSpace(item.Magnet)
            && (!requireUnexported || !item.IsExported)
            && !string.Equals(item.ProcessingStatus, ProcessingStatuses.Deleted, StringComparison.Ordinal);
    }

    public int SetChecked(IEnumerable<MagnetItem> items, bool isChecked, bool requireMatch = true)
    {
        var changedCount = 0;
        foreach (var item in items)
        {
            var shouldCheck = isChecked
                && IsEligibleForExport(item, true)
                && (!requireMatch || IsMatched(item));
            if (item.IsChecked == shouldCheck)
            {
                continue;
            }

            item.IsChecked = shouldCheck;
            _itemStore.Append(item);
            changedCount++;
        }

        return changedCount;
    }
}
