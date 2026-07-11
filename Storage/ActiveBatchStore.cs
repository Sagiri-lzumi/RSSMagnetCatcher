using RSSMagnetCatcher.Core.Models;

namespace RSSMagnetCatcher.Storage;

public sealed class ActiveBatchStore
{
    private readonly object _sync = new();
    private readonly JsonConfigStore _configStore;
    private readonly string _path;

    public ActiveBatchStore(JsonConfigStore configStore, string path)
    {
        _configStore = configStore;
        _path = path;
    }

    public ActiveBatch Load()
    {
        lock (_sync)
        {
            return _configStore.Load(_path, ActiveBatch.Empty());
        }
    }

    public void Save(ActiveBatch batch)
    {
        lock (_sync)
        {
            _configStore.Save(_path, batch);
        }
    }

    public void Clear()
    {
        Save(ActiveBatch.Empty());
    }
}
