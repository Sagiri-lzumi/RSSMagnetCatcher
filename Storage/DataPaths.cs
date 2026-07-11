namespace RSSMagnetCatcher.Storage;

public sealed class DataPaths
{
    public DataPaths(string? rootDirectory = null)
    {
        RootDirectory = Path.GetFullPath(rootDirectory ?? AppContext.BaseDirectory);
    }

    public string RootDirectory { get; }

    public string DataDirectory => Path.Combine(RootDirectory, "data");

    public string LogsDirectory => Path.Combine(DataDirectory, "logs");

    public string TorrentExportDirectory => Path.Combine(DataDirectory, "torrent_exports");

    public string SettingsFile => Path.Combine(DataDirectory, "app.settings.json");

    public string FeedsFile => Path.Combine(DataDirectory, "feeds.json");

    public string RulesFile => Path.Combine(DataDirectory, "rules.json");

    public string FeedStateFile => Path.Combine(DataDirectory, "feed_state.json");

    public string ItemCacheFile => Path.Combine(DataDirectory, "item_cache.jsonl");

    public string ExportHistoryFile => Path.Combine(DataDirectory, "export_history.jsonl");

    public string ActiveBatchFile => Path.Combine(DataDirectory, "active_batch.json");

    public string AppLogFile => Path.Combine(LogsDirectory, "app.log");

    public string ErrorLogFile => Path.Combine(LogsDirectory, "error.log");
}
