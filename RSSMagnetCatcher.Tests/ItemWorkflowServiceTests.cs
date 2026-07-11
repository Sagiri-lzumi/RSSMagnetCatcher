using RSSMagnetCatcher.Core.Models;
using RSSMagnetCatcher.Core.Services;
using RSSMagnetCatcher.Storage;

namespace RSSMagnetCatcher.Tests;

public sealed class ItemWorkflowServiceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "RRSMC.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void NormalizeCache_MigratesLegacyExportedAndUnexportedItems()
    {
        var (service, itemStore, _) = CreateService("(CHS)");
        itemStore.Append(CreateItem("used_old", "CHS 1080p", "aaa111", isExported: true));
        var filtered = CreateItem("filtered_old", "BIG5 720p", "bbb222", isExported: false);
        filtered.MatchStatus = MatchStatuses.Filtered;
        itemStore.Append(filtered);

        Assert.Equal(1, service.NormalizeCache());

        var latest = itemStore.LoadLatest().ToDictionary(item => item.Id);
        Assert.Equal(ProcessingStatuses.Used, latest["used_old"].ProcessingStatus);
        Assert.True(latest["used_old"].IsExported);
        Assert.Equal(ProcessingStatuses.Pending, latest["filtered_old"].ProcessingStatus);
        Assert.False(latest["filtered_old"].IsExported);
    }

    [Fact]
    public void StartBatch_WithRuleChecksMatchesAndCompletionDiscardsRemainder()
    {
        var (service, itemStore, _) = CreateService("(CHS);(1080p)");
        itemStore.Append(CreateItem("matched", "Example CHS 1080p", "aaa111"));
        itemStore.Append(CreateItem("unmatched", "Example BIG5 720p", "bbb222"));

        var result = service.StartBatch(
            itemStore.LoadLatest(),
            ItemListViewMode.Pending,
            null,
            useCurrentRule: true);

        Assert.True(result.Started);
        Assert.Equal(2, result.CandidateCount);
        Assert.Equal(1, result.CheckedCount);
        var afterBatch = itemStore.LoadLatest().ToDictionary(item => item.Id);
        Assert.True(afterBatch["matched"].IsChecked);
        Assert.False(afterBatch["unmatched"].IsChecked);

        var completion = service.CompleteCopy([afterBatch["matched"]]);

        Assert.Equal(1, completion.UsedCount);
        Assert.Equal(1, completion.DiscardedCount);
        var latest = itemStore.LoadLatest().ToDictionary(item => item.Id);
        Assert.Equal(ProcessingStatuses.Used, latest["matched"].ProcessingStatus);
        Assert.Equal(ProcessingStatuses.Discarded, latest["unmatched"].ProcessingStatus);
        Assert.False(service.ActiveBatch.IsActive);
    }

    [Fact]
    public void ManualCopyWithoutActiveBatch_DoesNotDiscardOtherPendingItems()
    {
        var (service, itemStore, _) = CreateService(string.Empty);
        itemStore.Append(CreateItem("copied", "One", "aaa111"));
        itemStore.Append(CreateItem("untouched", "Two", "bbb222"));
        var copied = itemStore.LoadLatest().Single(item => item.Id == "copied");

        var completion = service.CompleteCopy([copied]);

        Assert.Equal(1, completion.UsedCount);
        Assert.Equal(0, completion.DiscardedCount);
        var latest = itemStore.LoadLatest().ToDictionary(item => item.Id);
        Assert.Equal(ProcessingStatuses.Used, latest["copied"].ProcessingStatus);
        Assert.Equal(ProcessingStatuses.Pending, latest["untouched"].ProcessingStatus);
    }

    [Fact]
    public void CancelActiveBatch_RestoresOriginalCheckedStatesAndPersistsAcrossServiceInstances()
    {
        var (service, itemStore, paths) = CreateService("(CHS)");
        var originallyChecked = CreateItem("checked", "CHS 1080p", "aaa111");
        originallyChecked.IsChecked = true;
        itemStore.Append(originallyChecked);
        itemStore.Append(CreateItem("unchecked", "BIG5 720p", "bbb222"));

        Assert.True(service.StartBatch(itemStore.LoadLatest(), ItemListViewMode.Pending, null, useCurrentRule: false).Started);
        var reloadedService = CreateService(paths, "(CHS)").Service;

        Assert.True(reloadedService.ActiveBatch.IsActive);
        Assert.Equal(1, reloadedService.CancelActiveBatch());

        var latest = itemStore.LoadLatest().ToDictionary(item => item.Id);
        Assert.True(latest["checked"].IsChecked);
        Assert.False(latest["unchecked"].IsChecked);
        Assert.False(reloadedService.ActiveBatch.IsActive);
    }

    [Fact]
    public void DiscardedBatchCompletion_KeepsUncopiedItemsDiscarded()
    {
        var (service, itemStore, _) = CreateService("(CHS)");
        var copied = CreateItem("copied", "CHS 1080p", "aaa111");
        copied.ProcessingStatus = ProcessingStatuses.Discarded;
        var kept = CreateItem("kept", "BIG5 720p", "bbb222");
        kept.ProcessingStatus = ProcessingStatuses.Discarded;
        itemStore.Append(copied);
        itemStore.Append(kept);

        service.StartBatch(itemStore.LoadLatest(), ItemListViewMode.Discarded, null, true);
        var copiedLatest = itemStore.LoadLatest().Single(item => item.Id == "copied");
        service.CompleteCopy([copiedLatest]);

        var latest = itemStore.LoadLatest().ToDictionary(item => item.Id);
        Assert.Equal(ProcessingStatuses.Used, latest["copied"].ProcessingStatus);
        Assert.Equal(ProcessingStatuses.Discarded, latest["kept"].ProcessingStatus);
    }

    [Fact]
    public void CompleteExport_WithActiveBatchKeepsFailedSelectedItemsPending()
    {
        var (service, itemStore, _) = CreateService(string.Empty);
        itemStore.Append(CreateItem("saved", "Saved", "aaa111"));
        itemStore.Append(CreateItem("failed", "Failed", "bbb222"));
        itemStore.Append(CreateItem("unselected", "Unselected", "ccc333"));

        service.StartBatch(itemStore.LoadLatest(), ItemListViewMode.Pending, null, useCurrentRule: false);
        var afterBatch = itemStore.LoadLatest().ToDictionary(item => item.Id);
        afterBatch["unselected"].IsChecked = false;
        itemStore.Append(afterBatch["unselected"]);

        var completion = service.CompleteExport([afterBatch["saved"]], ["failed"]);

        Assert.Equal(1, completion.UsedCount);
        Assert.Equal(1, completion.DiscardedCount);
        var latest = itemStore.LoadLatest().ToDictionary(item => item.Id);
        Assert.Equal(ProcessingStatuses.Used, latest["saved"].ProcessingStatus);
        Assert.Equal(ProcessingStatuses.Pending, latest["failed"].ProcessingStatus);
        Assert.True(latest["failed"].IsChecked);
        Assert.Equal(ProcessingStatuses.Discarded, latest["unselected"].ProcessingStatus);
        Assert.False(service.ActiveBatch.IsActive);
    }

    [Fact]
    public void CompleteExport_WithNoSuccessfulItemsLeavesBatchActive()
    {
        var (service, itemStore, _) = CreateService(string.Empty);
        itemStore.Append(CreateItem("failed", "Failed", "bbb222"));

        service.StartBatch(itemStore.LoadLatest(), ItemListViewMode.Pending, null, useCurrentRule: false);
        var completion = service.CompleteExport([]);

        Assert.Equal(0, completion.UsedCount);
        Assert.True(service.ActiveBatch.IsActive);
        Assert.Equal(ProcessingStatuses.Pending, itemStore.LoadLatest().Single().ProcessingStatus);
    }

    [Fact]
    public void StartBatch_AllowsTorrentOnlyExportableItems()
    {
        var (service, itemStore, _) = CreateService(string.Empty);
        itemStore.Append(CreateTorrentOnlyItem("torrent_only"));

        var result = service.StartBatch(itemStore.LoadLatest(), ItemListViewMode.Pending, null, useCurrentRule: false);

        Assert.True(result.Started);
        Assert.True(itemStore.LoadLatest().Single().IsChecked);
    }

    [Fact]
    public void RestoreAndSoftDelete_UpdateDiscardedItems()
    {
        var (service, itemStore, _) = CreateService(string.Empty);
        var item = CreateItem("discarded", "Example", "aaa111");
        item.ProcessingStatus = ProcessingStatuses.Discarded;
        itemStore.Append(item);

        Assert.Equal(1, service.RestoreDiscarded(itemStore.LoadLatest()));
        Assert.Equal(ProcessingStatuses.Pending, itemStore.LoadLatest().Single().ProcessingStatus);

        Assert.Equal(1, service.SoftDelete(itemStore.LoadLatest()));
        var latest = itemStore.LoadLatest().Single();
        Assert.Equal(ProcessingStatuses.Deleted, latest.ProcessingStatus);
        Assert.False(latest.IsChecked);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }

    private (ItemWorkflowService Service, JsonlItemStore ItemStore, DataPaths Paths) CreateService(string expression)
    {
        var paths = new DataPaths(_tempDirectory);
        return CreateService(paths, expression);
    }

    private static (ItemWorkflowService Service, JsonlItemStore ItemStore, DataPaths Paths) CreateService(
        DataPaths paths,
        string expression)
    {
        var configStore = new JsonConfigStore();
        var itemStore = new JsonlItemStore(paths.ItemCacheFile);
        var currentFilter = new CurrentFilterService(
            new AppSettings { LastValidFilterExpression = expression },
            configStore,
            paths.SettingsFile,
            new RuleMatchService(),
            []);
        var itemFilter = new ItemFilterService(itemStore, new RuleMatchService(), currentFilter);
        var batchStore = new ActiveBatchStore(configStore, paths.ActiveBatchFile);
        return (new ItemWorkflowService(itemStore, batchStore, itemFilter), itemStore, paths);
    }

    private static MagnetItem CreateItem(
        string id,
        string title,
        string hash,
        bool isExported = false)
    {
        return new MagnetItem
        {
            Id = id,
            FeedId = "feed_a",
            Title = title,
            SearchText = title,
            Magnet = $"magnet:?xt=urn:btih:{hash}",
            InfoHash = hash,
            FoundAt = DateTimeOffset.Parse("2026-06-03T10:00:00+08:00"),
            IsExported = isExported,
            ProcessingStatus = ProcessingStatuses.Pending
        };
    }

    private static MagnetItem CreateTorrentOnlyItem(string id)
    {
        return new MagnetItem
        {
            Id = id,
            FeedId = "feed_a",
            Title = "Torrent only",
            SearchText = "Torrent only",
            TorrentUrl = "https://example.test/files/torrent_only.torrent",
            FoundAt = DateTimeOffset.Parse("2026-06-03T10:00:00+08:00"),
            MatchStatus = MatchStatuses.TorrentOnly,
            ProcessingStatus = ProcessingStatuses.Pending
        };
    }
}
