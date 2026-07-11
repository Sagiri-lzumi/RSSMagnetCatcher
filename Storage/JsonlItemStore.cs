using System.Text;
using System.Text.Json;
using RSSMagnetCatcher.Core.Models;

namespace RSSMagnetCatcher.Storage;

public sealed class JsonlItemStore
{
    private readonly object _sync = new();
    private readonly string _path;

    public JsonlItemStore(string path)
    {
        _path = path;
    }

    public IReadOnlyList<MagnetItem> LoadLatest()
    {
        lock (_sync)
        {
            if (!File.Exists(_path))
            {
                return [];
            }

            var latest = new Dictionary<string, MagnetItem>(StringComparer.Ordinal);
            foreach (var line in File.ReadLines(_path, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    var item = JsonSerializer.Deserialize<MagnetItem>(line, JsonDefaults.JsonlOptions);
                    if (item is not null && !string.IsNullOrWhiteSpace(item.Id))
                    {
                        latest[item.Id] = item;
                    }
                }
                catch (JsonException)
                {
                    // Keep loading valid snapshots if a single line was interrupted or corrupted.
                }
            }

            return latest.Values.ToList();
        }
    }

    public void Append(MagnetItem item)
    {
        lock (_sync)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var json = JsonSerializer.Serialize(item, JsonDefaults.JsonlOptions);
            File.AppendAllText(_path, json + Environment.NewLine, new UTF8Encoding(false));
        }
    }

    public void Rewrite(IEnumerable<MagnetItem> items)
    {
        lock (_sync)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tempPath = _path + ".tmp";
            using (var writer = new StreamWriter(tempPath, false, new UTF8Encoding(false)))
            {
                foreach (var item in items)
                {
                    writer.WriteLine(JsonSerializer.Serialize(item, JsonDefaults.JsonlOptions));
                }
            }

            File.Move(tempPath, _path, true);
        }
    }
}
