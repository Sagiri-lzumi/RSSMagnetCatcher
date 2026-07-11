using System.Text;
using RSSMagnetCatcher.Core.Models;

namespace RSSMagnetCatcher.Storage;

public sealed class DataInitializer
{
    private readonly DataPaths _paths;
    private readonly JsonConfigStore _configStore;

    public DataInitializer(DataPaths paths, JsonConfigStore configStore)
    {
        _paths = paths;
        _configStore = configStore;
    }

    public void Initialize()
    {
        Directory.CreateDirectory(_paths.LogsDirectory);
        Directory.CreateDirectory(_paths.TorrentExportDirectory);
        EnsureJson(_paths.SettingsFile, new AppSettings());
        EnsureJson(_paths.FeedsFile, CreateDefaultFeeds());
        EnsureJson(_paths.RulesFile, CreateDefaultRules());
        EnsureJson(_paths.FeedStateFile, new Dictionary<string, FeedState>());
        EnsureJson(_paths.ActiveBatchFile, ActiveBatch.Empty());
        EnsureEmptyFile(_paths.ItemCacheFile);
        EnsureEmptyFile(_paths.ExportHistoryFile);
        EnsureEmptyFile(_paths.AppLogFile);
        EnsureEmptyFile(_paths.ErrorLogFile);
    }

    private void EnsureJson<T>(string path, T value)
    {
        if (!File.Exists(path))
        {
            _configStore.Save(path, value);
        }
    }

    private static void EnsureEmptyFile(string path)
    {
        if (!File.Exists(path))
        {
            File.WriteAllText(path, string.Empty, new UTF8Encoding(false));
        }
    }

    private static List<FeedConfig> CreateDefaultFeeds()
    {
        return
        [
            new FeedConfig
            {
                Id = "feed_mikan_classic",
                Name = "Mikan Classic",
                Url = "https://mikanani.me/RSS/Classic",
                Group = "默认",
                DefaultRuleId = "rule_1080p_sc"
            }
        ];
    }

    private static List<FilterRule> CreateDefaultRules()
    {
        return
        [
            new FilterRule
            {
                Id = "rule_1080p_sc",
                Name = "简体 1080p 及以上",
                IncludeExpression = "(GBK|CHS|简|简体|SC);(1080p|1080i|1440p|2160p|4k|uhd)",
                ExcludeExpression = "720p|BIG5|CHT|繁|繁体"
            }
        ];
    }
}
