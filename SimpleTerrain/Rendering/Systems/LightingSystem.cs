namespace SimpleTerrain.Rendering.Systems;

using World;

public class LightingSystem
{
    private readonly List<DirectionalLight> _directional = new();
    private readonly List<PointLight>       _point       = new();
    private readonly List<SpotLight>        _spot        = new();

    public IReadOnlyList<DirectionalLight> DirectionalLights => _directional;
    public IReadOnlyList<PointLight>       PointLights       => _point;
    public IReadOnlyList<SpotLight>        SpotLights        => _spot;
    
    public bool IsDirty { get; private set; } = true;

    public void MarkDirty() => IsDirty = true;
    public void ClearDirty() => IsDirty = false;

    
    public void Add(DirectionalLight light)
    {
        _directional.Add(light);
        MarkDirty();
    }

    public void Add(PointLight light)
    {
        _point.Add(light);
        MarkDirty();
    }

    public void Add(SpotLight light)
    {
        _spot.Add(light);
        MarkDirty();
    }

    public void Remove(DirectionalLight light)
    {
        _directional.Remove(light);
        MarkDirty();
    }

    public void Remove(PointLight light)
    {
        _point.Remove(light);
        MarkDirty();
    }

    public void Remove(SpotLight light)
    {
        _spot.Remove(light);
        MarkDirty();
    }
}