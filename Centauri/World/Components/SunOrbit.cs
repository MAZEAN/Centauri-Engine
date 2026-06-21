namespace Centauri.World.Components;

using System.Numerics;

// Spins the directional light to demo the update pass — the seed of a day/night cycle.
public sealed class SunOrbit : Component
{
    private readonly float _speed;   // radians/sec
    public SunOrbit(float speed = 0.2f) => _speed = speed;

    public override void Update(float dt)
    {
        if (Owner.Light is not DirectionalLight sun) return;

        var rot = Matrix4x4.CreateRotationX(_speed * dt);
        sun.Direction = Vector3.Normalize(Vector3.TransformNormal(sun.Direction, rot));
    }
}