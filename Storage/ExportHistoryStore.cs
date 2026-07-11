using System.Text;
using System.Text.Json;
using RSSMagnetCatcher.Core.Models;

namespace RSSMagnetCatcher.Storage;

public sealed class ExportHistoryStore
{
    private readonly object _sync = new();
    private readonly string _path;

    public ExportHistoryStore(string path)
    {
        _path = path;
    }

    public void Append(ExportHistoryEntry entry)
    {
        lock (_sync)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var json = JsonSerializer.Serialize(entry, JsonDefaults.JsonlOptions);
            File.AppendAllText(_path, json + Environment.NewLine, new UTF8Encoding(false));
        }
    }

    public IReadOnlyList<ExportHistoryEntry> Load()
    {
        lock (_sync)
        {
            if (!File.Exists(_path))
            {
                return [];
            }

            var entries = new List<ExportHistoryEntry>();
            foreach (var line in File.ReadLines(_path, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    var entry = JsonSerializer.Deserialize<ExportHistoryEntry>(line, JsonDefaults.JsonlOptions);
                    if (entry is not null)
                    {
                        entries.Add(entry);
                    }
                }
                catch (JsonException)
                {
                    // Keep loading valid records if a single line was interrupted or corrupted.
                }
            }

            return entries;
        }
    }

    public void Rewrite(IEnumerable<ExportHistoryEntry> entries)
    {
        lock (_sync)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tempPath = _path + ".tmp";
            using (var writer = new StreamWriter(tempPath, false, new UTF8Encoding(false)))
            {
                foreach (var entry in entries)
                {
                    writer.WriteLine(JsonSerializer.Serialize(entry, JsonDefaults.JsonlOptions));
                }
            }

            File.Move(tempPath, _path, true);
        }
    }

    public void Clear()
    {
        Rewrite([]);
    }
}
