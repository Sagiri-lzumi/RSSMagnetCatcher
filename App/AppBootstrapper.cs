using System.Net.Http.Headers;
using RSSMagnetCatcher.Core.Models;
using RSSMagnetCatcher.Core.Services;
using RSSMagnetCatcher.Infrastructure;
using RSSMagnetCatcher.Storage;
using RSSMagnetCatcher.UI;

namespace RSSMagnetCatcher.App;

public static class AppBootstrapper
{
    public static TrayApplicationContext Build()
    {
        var paths = new DataPaths();
        var configStore = new JsonConfigStore();
        new DataInitializer(paths, configStore).Initialize();

        var logger = new Logger(paths.AppLogFile, paths.ErrorLogFile);
        var settings = configStore.Load(paths.SettingsFile, new AppSettings());
        var itemStore = new JsonlItemStore(paths.ItemCacheFile);
        var stateStore = new FeedStateStore(configStore, paths.FeedStateFile);
        var historyStore = new ExportHistoryStore(paths.ExportHistoryFile);
        var batchStore = new ActiveBatchStore(configStore, paths.ActiveBatchFile);
        var ruleMatchService = new RuleMatchService();
        var rulePresetService = new RulePresetService(configStore, paths.RulesFile, ruleMatchService);
        var rules = rulePresetService.Load();
        var diagnosticsService = new FeedDiagnosticsService();
        var cacheMaintenanceService = new CacheMaintenanceService(itemStore, historyStore, batchStore, settings);
        var startupManager = new StartupManager();
        var extractService = new MagnetExtractService();
        var currentFilterService = new CurrentFilterService(
            settings,
            configStore,
            paths.SettingsFile,
            ruleMatchService,
            rules);
        var itemFilterService = new ItemFilterService(itemStore, ruleMatchService, currentFilterService);
        var itemWorkflowService = new ItemWorkflowService(itemStore, batchStore, itemFilterService);
        itemWorkflowService.NormalizeCache();
        cacheMaintenanceService.Compact();
        var itemListQueryService = new ItemListQueryService();
        var backfillService = new TorrentMagnetBackfillService(
            itemStore,
            stateStore,
            extractService,
            itemFilterService);
        var backfillResult = backfillService.Backfill(
            configStore.Load(paths.FeedsFile, new List<FeedConfig>()));
        if (backfillResult.UpgradedItems > 0)
        {
            logger.Info($"已从 torrent URL 回填 {backfillResult.UpgradedItems} 条 magnet。");
        }

        var httpClient = CreateHttpClient();
        var torrentExportService = new TorrentExportService(paths, httpClient);
        var rssFetchService = new RssFetchService(httpClient);
        var mikanHistoryService = new MikanHistoryService(rssFetchService);
        var feedCheckService = new FeedCheckService(
            rssFetchService,
            new RssParseService(),
            extractService,
            itemStore,
            stateStore,
            logger,
            settings,
            ruleMatchService,
            currentFilterService,
            diagnosticsService,
            mikanHistoryService);
        var scheduler = new FeedScheduler(
            () => configStore.Load(paths.FeedsFile, new List<FeedConfig>()),
            stateStore,
            feedCheckService,
            settings,
            logger);
        var statusService = new ApplicationStatusService();
        var mainForm = new MainForm(
            paths,
            configStore,
            itemStore,
            historyStore,
            stateStore,
            new ClipboardExportService(),
            torrentExportService,
            currentFilterService,
            itemFilterService,
            itemWorkflowService,
            itemListQueryService,
            scheduler,
            statusService,
            settings,
            cacheMaintenanceService,
            diagnosticsService,
            rulePresetService,
            ruleMatchService,
            startupManager);

        logger.Info("应用程序启动。");
        return new TrayApplicationContext(
            mainForm,
            scheduler,
            statusService,
            settings,
            rulePresetService,
            httpClient);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RRSMC", "1.0"));
        return client;
    }
}
