namespace Centauri.Loading;

using System.Numerics;
using System.Text.Json;

using World.Components;
using Simulation.Physics;

// Builds Components from their entity-set JSON definitions (EntityDefinition.Components, or an
// environment's "sun"). Adding a new authorable behavior is one line here plus the Component
// subclass — no per-type JSON plumbing.
public static class ComponentFactory
{
    private static readonly Dictionary<string, Func<ComponentDefinition, Component>> Registry =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["sunOrbit"] = d => new SunOrbit(d.Float("speed", 0.2f)),

            ["dayNight"] = d => new DayNightCycle(
                speed:        d.Float("speed", 0.02f),
                startTime:    d.Float("startTime", 0.3f),
                dayIntensity: d.Float("dayIntensity", 4f),
                dayColor:     d.Vector3("dayColor"),
                duskColor:    d.Vector3("duskColor")),

            ["rigidBody"] = d => new RigidBody
            {
                Kind = d.String("kind", "dynamic") switch
                {
                    "static"    => BodyKind.Static,
                    "kinematic" => BodyKind.Kinematic,
                    _           => BodyKind.Dynamic,
                },
                Shape = d.String("shape", "box") switch
                {
                    "sphere"  => BodyShape.Sphere,
                    "capsule" => BodyShape.Capsule,
                    "mesh"    => BodyShape.Mesh,
                    _         => BodyShape.Box,
                },
                Mass     = d.Float("mass", 1f),
                Friction = d.Float("friction", 1f),
            },
        };

    public static Component Create(ComponentDefinition def)
    {
        if (!Registry.TryGetValue(def.Type, out var make))
            throw new Exception($"Unknown component type '{def.Type}'.");

        var component = make(def);
        component.Enabled = def.Enabled;
        return component;
    }
}

internal static class ComponentParams
{
    public static float Float(this ComponentDefinition d, string key, float fallback) =>
        d.Params is not null
        && d.Params.TryGetValue(key, out var v)
        && v.ValueKind == JsonValueKind.Number
            ? v.GetSingle()
            : fallback;

    public static string String(this ComponentDefinition d, string key, string fallback) =>
        d.Params is not null
        && d.Params.TryGetValue(key, out var v)
        && v.ValueKind == JsonValueKind.String
            ? v.GetString()!.ToLowerInvariant()
            : fallback;

    public static Vector3? Vector3(this ComponentDefinition d, string key)
    {
        if (d.Params is null
            || !d.Params.TryGetValue(key, out var v)
            || v.ValueKind != JsonValueKind.Array)
            return null;

        var a = v.EnumerateArray().Select(e => e.GetSingle()).ToArray();
        return a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : null;
    }
}