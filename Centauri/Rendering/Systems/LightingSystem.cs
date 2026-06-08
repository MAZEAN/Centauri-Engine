namespace Centauri.Rendering.Systems;

using World;

public class LightingSystem
{
    private readonly List<DirectionalLight> _directional = new();
    private readonly List<PointLight>       _point       = new();
    private readonly List<SpotLight>        _spot        = new();

    public IReadOnlyList<DirectionalLight> DirectionalLights => _directional;
    public IReadOnlyList<PointLight>       PointLights       => _point;
    public IReadOnlyList<SpotLight>        SpotLights        => _spot;
    
    public void Add(DirectionalLight light)
    {
        _directional.Add(light);
    }

    public void Add(PointLight light)
    {
        _point.Add(light);
    }

    public void Add(SpotLight light)
    {
        _spot.Add(light);
    }

    public void Remove(DirectionalLight light)
    {
        _directional.Remove(light);
    }

    public void Remove(PointLight light)
    {
        _point.Remove(light);
    }

    public void Remove(SpotLight light)
    {
        _spot.Remove(light);
    }
}