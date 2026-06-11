namespace Centauri.Utils.Caching;

public sealed class AssetCache<T> : IDisposable where T : class, IDisposable
{
    private readonly Dictionary<string, T> _assets = new();
    private readonly Func<string, T> _factory;

    public AssetCache(Func<string, T> factory)
    {
        _factory = factory;
    }

    public T Get(string key)
    {
        if (_assets.TryGetValue(key, out var asset))
            return asset;                 // one instance per key, ever

        asset = _factory(key);
        _assets[key] = asset;
        return asset;
    }

    public void Dispose()
    {
        foreach (var asset in _assets.Values)
            asset.Dispose();

        _assets.Clear();
    }
}