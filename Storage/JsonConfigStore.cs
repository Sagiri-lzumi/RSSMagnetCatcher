using System.Text;
using System.Text.Json;

namespace RSSMagnetCatcher.Storage;

public sealed class JsonConfigStore
{
    public T Load<T>(string path, T fallback)
    {
        if (!File.Exists(path))
        {
            return fallback;
        }

        var json = File.ReadAllText(path, Encoding.UTF8);
        return JsonSerializer.Deserialize<T>(json, JsonDefaults.Options) ?? fallback;
    }

    public void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(value, JsonDefaults.Options);
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, json + Environment.NewLine, new UTF8Encoding(false));
        File.Move(tempPath, path, true);
    }
}
