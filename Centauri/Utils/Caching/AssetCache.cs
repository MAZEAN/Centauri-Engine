namespace Centauri.Utils.Caching;

public sealed class AssetCache<T> : IDisposable where T : class, IDisposable
{
    private readonly LRUCache<string, T> _cache;
    private readonly Func<string, T> _factory;
    private readonly HashSet<T> _all = new();

    public AssetCache(int capacity, Func<string, T> factory)
    {
        _cache = new LRUCache<string, T>(capacity);
        _factory = factory;
    }

    public T Get(string key)
    {
        var asset = _cache.Get(key);
        if (asset != null)
            return asset;

        asset = _factory(key);

        _cache.Put(key, asset);
        _all.Add(asset);

        return asset;
    }

    public void Dispose()
    {
        foreach (var asset in _all)
            asset.Dispose();

        _all.Clear();
    }
}