using RSSMagnetCatcher.Core.Models;

namespace RSSMagnetCatcher.Storage;

public sealed class FeedStateStore
{
    private readonly JsonConfigStore _configStore;
    private readonly string _path;

    public FeedStateStore(JsonConfigStore configStore, string path)
    {
        _configStore = configStore;
        _path = path;
    }

    public Dictionary<string, FeedState> Load()
    {
        return _configStore.Load(_path, new Dictionary<string, FeedState>());
    }

    public void Set(string feedId, FeedState state)
    {
        var states = Load();
        states[feedId] = state;
        _configStore.Save(_path, states);
    }

    public void Remove(string feedId)
    {
        var states = Load();
        if (states.Remove(feedId))
        {
            _configStore.Save(_path, states);
        }
    }
}
