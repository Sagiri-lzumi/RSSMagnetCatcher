using RSSMagnetCatcher.Core.Models;
using RSSMagnetCatcher.Core.Services;
using RSSMagnetCatcher.Storage;

namespace RSSMagnetCatcher.Tests;

public sealed class CacheMaintenanceServiceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "RRSMC.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Compact_RemovesExpiredExportedItemsAndKeepsUnexportedItems()
    {
        var now = DateTimeOffset.Parse("2026-06-02T10:00:00+08:00");
        var itemStore = new JsonlItemStore(Path.Combine(_tempDirectory, "item_cache.jsonl"));
        var historyStore = new ExportHistoryStore(Path.Combine(_tempDirectory, "export_history.jsonl"));
        itemStore.Append(CreateItem("unexported", now.AddDays(-100), false));
        itemStore.Append(CreateItem("expired", now.AddDays(-20), true));
        itemStore.Append(CreateItem("recent", now.AddDays(-1), true));
        historyStore.Append(CreateHistory("expired", now.AddDays(-20)));
        historyStore.Append(CreateHistory("recent", now.AddDays(-1)));
        var service = new CacheMaintenanceService(
            itemStore,
            historyStore,
            CreateBatchStore(),
            new AppSettings { KeepHistoryDays = 7, MaxCacheItems = 2 });

        var result = service.Compact(now);

        Assert.Equal(1, result.RemovedItems);
        Assert.Equal(["recent", "unexported"], itemStore.LoadLatest().Select(item => item.Id).Order());
        Assert.Equal("recent", Assert.Single(historyStore.Load()).ItemId);
    }

    [Fact]
    public void CleanExportedHistory_PreservesEveryUnexportedItem()
    {
        var itemStore = new JsonlItemStore(Path.Combine(_tempDirectory, "item_cache.jsonl"));
        var historyStore = new ExportHistoryStore(Path.Combine(_tempDirectory, "export_history.jsonl"));
        itemStore.Append(CreateItem("keep", DateTimeOffset.Now, false));
        itemStore.Append(CreateItem("remove", DateTimeOffset.Now, true));
        historyStore.Append(CreateHistory("remove", DateTimeOffset.Now));
        var service = new CacheMaintenanceService(itemStore, historyStore, CreateBatchStore(), new AppSettings());

        var result = service.CleanExportedHistory();

        Assert.Equal(1, result.RemovedItems);
        Assert.Equal("keep", Assert.Single(itemStore.LoadLatest()).Id);
        Assert.Empty(historyStore.Load());
    }

    [Fact]
    public void Compact_PreservesUnexportedItemsEvenWhenTheyExceedMaximum()
    {
        var itemStore = new JsonlItemStore(Path.Combine(_tempDirectory, "item_cache.jsonl"));
        var historyStore = new ExportHistoryStore(Path.Combine(_tempDirectory, "export_history.jsonl"));
        itemStore.Append(CreateItem("one", DateTimeOffset.Now, false));
        itemStore.Append(CreateItem("two", DateTimeOffset.Now, false));
        var service = new CacheMaintenanceService(
            itemStore,
            historyStore,
            CreateBatchStore(),
            new AppSettings { MaxCacheItems = 1 });

        service.Compact();

        Assert.Equal(2, itemStore.LoadLatest().Count);
    }

    [Fact]
    public void Compact_LimitsExportedArticlesPerFeedAndAlwaysKeepsUnexportedItems()
    {
        var now = DateTimeOffset.Parse("2026-06-02T10:00:00+08:00");
        var itemStore = new JsonlItemStore(Path.Combine(_tempDirectory, "item_cache.jsonl"));
        var historyStore = new ExportHistoryStore(Path.Combine(_tempDirectory, "export_history.jsonl"));
        itemStore.Append(new MagnetItem
        {
            Id = "protected",
            FeedId = "feed_a",
            FoundAt = now,
            Magnet = "magnet:?xt=urn:btih:protected",
            ProcessingStatus = ProcessingStatuses.Pending
        });
        for (var index = 0; index < 120; index++)
        {
            var item = new MagnetItem
            {
                Id = $"exported_{index}",
                FeedId = "feed_a",
                FoundAt = now.AddMinutes(-index),
                IsExported = true,
                ProcessingStatus = ProcessingStatuses.Used
            };
            itemStore.Append(item);
            historyStore.Append(CreateHistory(item.Id, item.FoundAt));
        }

        var service = new CacheMaintenanceService(
            itemStore,
            historyStore,
            CreateBatchStore(),
            new AppSettings { KeepHistoryDays = 365, MaxCacheItems = 1000, MaxArticlesPerFeed = 100 });

        service.Compact(now);

        var retained = itemStore.LoadLatest();
        Assert.Equal(100, retained.Count);
        Assert.Contains(retained, item => item.Id == "protected");
        Assert.DoesNotContain(retained, item => item.Id == "exported_119");
    }

    [Fact]
    public void Compact_ProtectsActiveBatchItems()
    {
        var now = DateTimeOffset.Parse("2026-06-02T10:00:00+08:00");
        var itemStore = new JsonlItemStore(Path.Combine(_tempDirectory, "item_cache.jsonl"));
        var historyStore = new ExportHistoryStore(Path.Combine(_tempDirectory, "export_history.jsonl"));
        var batchStore = CreateBatchStore();
        var activeUsed = CreateItem("active_used", now.AddDays(-100), true);
        var expiredUsed = CreateItem("expired_used", now.AddDays(-100), true);
        itemStore.Append(activeUsed);
        itemStore.Append(expiredUsed);
        historyStore.Append(CreateHistory("active_used", now.AddDays(-100)));
        historyStore.Append(CreateHistory("expired_used", now.AddDays(-100)));
        batchStore.Save(new ActiveBatch
        {
            IsActive = true,
            Id = "batch_test",
            SourceProcessingStatus = ProcessingStatuses.Pending,
            CreatedAt = now,
            ItemIds = ["active_used"]
        });
        var service = new CacheMaintenanceService(
            itemStore,
            historyStore,
            batchStore,
            new AppSettings { KeepHistoryDays = 7, MaxCacheItems = 1 });

        service.Compact(now);

        Assert.Equal("active_used", Assert.Single(itemStore.LoadLatest()).Id);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }

    private static MagnetItem CreateItem(string id, DateTimeOffset foundAt, bool isExported)
    {
        return new MagnetItem
        {
            Id = id,
            FeedId = "feed_a",
            Magnet = $"magnet:?xt=urn:btih:{id}",
            FoundAt = foundAt,
            IsExported = isExported,
            ProcessingStatus = isExported ? ProcessingStatuses.Used : ProcessingStatuses.Pending
        };
    }

    private static ExportHistoryEntry CreateHistory(string itemId, DateTimeOffset exportedAt)
    {
        return new ExportHistoryEntry { ItemId = itemId, ExportedAt = exportedAt };
    }

    private ActiveBatchStore CreateBatchStore()
    {
        return new ActiveBatchStore(new JsonConfigStore(), Path.Combine(_tempDirectory, "active_batch.json"));
    }
}
