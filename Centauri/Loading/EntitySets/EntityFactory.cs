namespace Centauri.Loading;

using System.Numerics;

using Graphics.Resources.Materials;
using Graphics.Geometry;
using Rendering;
using World;

// Builds a live Entity from an EntityDefinition — model/material resolution, transform, light,
// components. Shared by EntitySetLoader (entity-set files, CreateEntity) and EnvironmentLoader
// (the environment's own optional "sun"), since both need the exact same construction, just from
// definitions that live in different files for different reasons.
public class EntityFactory
{
    private readonly ResourceSystem _resourceSystem;

    public EntityFactory(ResourceSystem resourceSystem) => _resourceSystem = resourceSystem;

    public Entity Build(EntityDefinition e)
    {
        var entity = BuildEntity(e);
        ApplyTransform(entity, e);
        ApplyComponents(entity, e);
        return entity;
    }

    private Entity BuildEntity(EntityDefinition e)
    {
        var model = !string.IsNullOrEmpty(e.Model)
            ? _resourceSystem.GetModel(e.Model)
            : null;

        var materials = ResolveMaterials(e, model);
        var light = e.Light is { } l ? CreateLight(l) : null;

        return new Entity(model, materials, light)
        {
            Name     = e.Name,
            UvScale  = new Vector2(e.UvScale[0],  e.UvScale[1]),
            UvOffset = new Vector2(e.UvOffset[0], e.UvOffset[1]),
            Enabled  = e.Enabled
        };
    }

    private static void ApplyTransform(Entity entity, EntityDefinition e)
    {
        entity.Transform.Position = new Vector3(e.Position[0], e.Position[1], e.Position[2]);
        entity.Transform.Scale    = new Vector3(e.Scale[0],    e.Scale[1],    e.Scale[2]);

        if (e.Rotation is { Length: 3 })
            entity.Transform.SetEulerAngles(e.Rotation[0], e.Rotation[1], e.Rotation[2]);

        entity.Authored = new TransformSnapshot(
            entity.Transform.Position,
            entity.Transform.EulerAngles,
            entity.Transform.Scale
        );
    }

    private static void ApplyComponents(Entity entity, EntityDefinition e)
    {
        if (e.Components is not { Count: > 0 }) return;

        foreach (var c in e.Components)
            entity.AddComponent(ComponentFactory.Create(c));
    }

    private Material?[] ResolveMaterials(EntityDefinition e, Model? model)
    {
        if (model is null)
            return Array.Empty<Material?>();

        var count = model.Meshes.Count;
        var result = new Material?[count];

        // Priority: the entity's own binding, else its singular "material", else whatever the
        // placed model declares as its default (Assets/Objects/**/*.model) — so placing the
        // same model repeatedly doesn't require repeating its material list every time.
        var modelDef = !string.IsNullOrEmpty(e.Model) ? _resourceSystem.GetModelDefinition(e.Model) : null;
        var binding = e.Materials
                      ?? (!string.IsNullOrEmpty(e.Material) ? new MaterialBinding { Indexed = [e.Material!] } : null)
                      ?? modelDef?.Materials;

        // Overrides the resolved material's own triplanar setting for this entity, entity value
        // taking priority over the model's default. Rarely set — see EntityDefinition's comment.
        var triplanar      = e.TriplanarOverride      ?? modelDef?.TriplanarOverride;
        var triplanarScale = e.TriplanarScaleOverride ?? modelDef?.TriplanarScaleOverride;

        Material Resolve(string path)
        {
            var mat = _resourceSystem.GetMaterial(path);
            if (triplanar is null && triplanarScale is null)
                return mat;

            var overridden = mat.Clone();
            if (triplanar is { } t) overridden.Triplanar = t;
            if (triplanarScale is { } s) overridden.TriplanarScale = s;
            return overridden;
        }

        // Named binding: matched to each mesh's name from the model file, not array position —
        // no need to know/verify mesh order inside the exported model.
        if (binding?.Named is { Count: > 0 } named)
        {
            for (var i = 0; i < count; i++)
            {
                var meshName = model.Meshes[i].Name;
                result[i] = !string.IsNullOrEmpty(meshName) && named.TryGetValue(meshName, out var path)
                    ? Resolve(path)
                    : _resourceSystem.DefaultMaterial;
            }
            return result;
        }

        var paths = binding?.Indexed is { Length: > 0 } indexed ? indexed : null;

        for (var i = 0; i < count; i++)
            result[i] = paths is null
                ? _resourceSystem.DefaultMaterial
                : Resolve(paths[Math.Min(i, paths.Length - 1)]);

        return result;
    }

    private static Light CreateLight(LightDefinition l)
    {
        var color     = new Vector3(l.Color[0], l.Color[1], l.Color[2]);
        var direction = new Vector3(l.Direction[0], l.Direction[1], l.Direction[2]);

        return l.Type.ToLowerInvariant() switch
        {
            "directional" => new DirectionalLight
            {
                Color = color, Intensity = l.Intensity, Enabled = l.Enabled,
                Direction = direction
            },
            "spot" => new SpotLight
            {
                Color = color, Intensity = l.Intensity, Enabled = l.Enabled,
                Direction = direction,
                InnerCutoff = l.InnerCutoff, OuterCutoff = l.OuterCutoff,
                CastsShadow = l.CastsShadow, Range = l.Range
            },
            "point" => new PointLight
            {
                Color = color, Intensity = l.Intensity, Enabled = l.Enabled,
                Constant = l.Constant, Linear = l.Linear, Quadratic = l.Quadratic
            },
            _ => throw new Exception($"Unknown light type '{l.Type}'.")
        };
    }
}
