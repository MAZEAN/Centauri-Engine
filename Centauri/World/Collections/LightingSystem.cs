namespace Centauri.World.Collections;

using System.Numerics;
using World;

public class LightingSystem
{
    public readonly struct ActivePoint(PointLight light, Vector3 position)
    {
        public readonly PointLight Light    = light;
        public readonly Vector3    Position = position;
    }

    public readonly struct ActiveSpot(SpotLight light, Vector3 position)
    {
        public readonly SpotLight Light    = light;
        public readonly Vector3   Position = position;
    }

    private readonly List<DirectionalLight> _directional = [];
    private readonly List<ActivePoint>      _point       = [];
    private readonly List<ActiveSpot>       _spot        = [];

    public IReadOnlyList<DirectionalLight> DirectionalLights => _directional;
    public IReadOnlyList<ActivePoint>      PointLights       => _point;
    public IReadOnlyList<ActiveSpot>       SpotLights        => _spot;
    
    public void Collect(IReadOnlyList<Entity> entities)
    {
        _directional.Clear();
        _point.Clear();
        _spot.Clear();

        foreach (var e in entities)
        {
            if (e.Light is not { Enabled: true } light)
                continue;

            switch (light)
            {
                case DirectionalLight d:
                    _directional.Add(d);
                    break;
                case SpotLight s:
                    _spot.Add(new ActiveSpot(s, e.Transform.WorldPosition));
                    break;
                case PointLight p:
                    _point.Add(new ActivePoint(p, e.Transform.WorldPosition));
                    break;
            }
        }
    }
}