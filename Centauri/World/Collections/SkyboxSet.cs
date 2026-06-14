namespace Centauri.World.Collections;

using Rendering.Resources;

public sealed class SkyboxSet
{
    private readonly List<GLCubemap> _items = new();
    private readonly Dictionary<string, GLCubemap> _byName = new();

    public GLCubemap? Active { get; private set; }
    public IReadOnlyDictionary<string, GLCubemap> ByName => _byName;
    public int Count => _items.Count;

    public void Add(string name, GLCubemap cubemap)
    {
        _byName[name] = cubemap;
        _items.Add(cubemap);
        Active ??= cubemap;                          // first added becomes active
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