namespace Centauri.World.Collections;

using Graphics.Resources;

public sealed class SkyboxSet
{
    private readonly List<GLTexture> _items = new();
    private readonly Dictionary<string, GLTexture> _byName = new();

    public GLTexture? Active { get; private set; }
    public IReadOnlyDictionary<string, GLTexture> ByName => _byName;
    public int Count => _items.Count;

    public void Add(string name, GLTexture panorama)
    {
        _byName[name] = panorama;
        _items.Add(panorama);
        Active ??= panorama;                          // first added becomes active
    }

    public void SetActive(string name)
    {
        if (!_byName.TryGetValue(name, out var sky))
            throw new Exception($"Skybox '{name}' not found.");
        Active = sky;
    }

    public void Cycle()
    {
        if (_items.Count == 0) return;
        var i = Active is null ? 0 : (_items.IndexOf(Active) + 1) % _items.Count;
        Active = _items[i];
    }
}