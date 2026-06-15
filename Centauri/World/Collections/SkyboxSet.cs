namespace Centauri.World.Collections;

using Graphics.Resources;

// A loaded skybox: its panorama texture plus an exposure multiplier
// applied before tonemapping (only meaningful for HDR panoramas).
public readonly record struct Skybox(GLTexture Texture, float Exposure);

public sealed class SkyboxSet
{
    private readonly List<Skybox> _items = new();
    private readonly Dictionary<string, Skybox> _byName = new();

    public Skybox? Active { get; private set; }
    public IReadOnlyDictionary<string, Skybox> ByName => _byName;
    public int Count => _items.Count;

    public void Add(string name, GLTexture panorama, float exposure = 1.0f)
    {
        var sky = new Skybox(panorama, exposure);
        _byName[name] = sky;
        _items.Add(sky);
        Active ??= sky;                               // first added becomes active
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
        var i = Active is { } a ? (_items.IndexOf(a) + 1) % _items.Count : 0;
        Active = _items[i];
    }
}