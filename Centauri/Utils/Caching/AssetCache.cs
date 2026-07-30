namespace Centauri.Utils.Caching;

public sealed class AssetCache<T> : IDisposable where T : class, IDisposable
{
    private readonly Func<string, T> _factory;

    private readonly Dictionary<string, T> _assets = new();

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

    // Whether `key` has already been resolved (via Get or Insert) without triggering the
    // factory — lets a caller that needs a *different* construction path than the factory's own
    // (e.g. GLTexture role-based compression eligibility, see ResourceSystem.GetTexture) check
    // first, Insert its own instance if it's a miss, then fall through to Get for the now-cached
    // hit rather than duplicating Get's own dictionary bookkeeping.
    public bool Contains(string key) => _assets.ContainsKey(key);

    public void Insert(string key, T value) => _assets[key] = value;

    public void Dispose()
    {
        foreach (var asset in _assets.Values)
            asset.Dispose();

        _assets.Clear();
    }
}