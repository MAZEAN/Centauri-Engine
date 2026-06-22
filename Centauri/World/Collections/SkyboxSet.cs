namespace Centauri.World.Collections;

using Graphics.Resources;

// A loaded skybox: its panorama texture plus the tonemapping controls applied
// in the skybox shader. Exposure and black level are only meaningful for HDR
// panoramas. Mutable (a class, not a record struct) so the inspector can tune
// the live instance and the renderer picks it up the same frame.
public class Skybox(GLTexture texture, float exposure, float blackLevel)
{
    public GLTexture Texture    { get; }      = texture;
    public float     Exposure   { get; set; } = exposure;
    public float     BlackLevel { get; set; } = blackLevel;
    
    public float AuthoredExposure   { get; } = exposure;
    public float AuthoredBlackLevel { get; } = blackLevel;
    
    // baked IBL (0 until baked)
    public uint IrradianceMap { get; set; }
    public uint PrefilteredMap { get; set; }
    public bool IblBaked => IrradianceMap != 0;
}

public class SkyboxSet
{
    private readonly List<Skybox> _items = new();
    private readonly Dictionary<string, Skybox> _byName = new();
    public Skybox? Active { get; private set; }
    
    public IReadOnlyList<Skybox> All => _items;

    public void Add(string name, GLTexture panorama, float exposure = 1.0f, float blackLevel = 0.0f)
    {
        var sky = new Skybox(panorama, exposure, blackLevel);
        _byName[name] = sky;
        _items.Add(sky);
        Active ??= sky;
    }

    public void SetActive(string name)
    {
        if (!_byName.TryGetValue(name, out var sky))
            throw new Exception($"Skybox '{name}' not found.");
        Active = sky;
    }
    
    public bool TrySetActive(string name)
    {
        if (!_byName.TryGetValue(name, out var sky)) return false;
        
        Active = sky;
        return true;
    }

    public void Cycle()
    {
        if (_items.Count == 0) return;
        var i = Active is { } a ? (_items.IndexOf(a) + 1) % _items.Count : 0;
        Active = _items[i];
    }
}