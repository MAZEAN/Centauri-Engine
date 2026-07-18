namespace Centauri.Loading;

using World;

// Converts a live Entity (plus the EntityDefinition it was originally built from, for the
// fields the inspector doesn't round-trip live — Model/Components/Triplanar overrides) into a
// fresh EntityDefinition ready to serialize. Pure conversion: the caller resolves the parent
// entity's name (TrackedEntitySet.FindOwner needs the full tracked set, which this class has no
// reason to know about) and passes it in.
internal static class EntityDefinitionWriter
{
    public static EntityDefinition Write(Entity entity, EntityDefinition source, string? parentName)
    {
        var t = entity.Transform;

        return new EntityDefinition
        {
            Name      = entity.Name,
            Model     = source.Model,
            Material  = source.Material,
            Materials = source.Materials,
            Position  = [t.Position.X, t.Position.Y, t.Position.Z],
            Scale     = [t.Scale.X, t.Scale.Y, t.Scale.Z],
            Rotation  = [t.EulerAngles.X, t.EulerAngles.Y, t.EulerAngles.Z],
            Enabled   = entity.Enabled,
            Light     = entity.Light is { } l ? ToDefinition(l) : null,
            Components = source.Components,
            TriplanarOverride      = source.TriplanarOverride,
            TriplanarScaleOverride = source.TriplanarScaleOverride,
            Parent    = parentName,
        };
    }

    private static LightDefinition ToDefinition(Light l) => l switch
    {
        DirectionalLight d => new LightDefinition
        {
            Type = "directional",
            Enabled = d.Enabled,
            Color = [d.Color.X, d.Color.Y, d.Color.Z],
            Intensity = d.Intensity,
            Direction = [d.Direction.X, d.Direction.Y, d.Direction.Z]
        },
        SpotLight sp => new LightDefinition
        {
            Type = "spot",
            Enabled = sp.Enabled,
            Color = [sp.Color.X, sp.Color.Y, sp.Color.Z],
            Intensity = sp.Intensity,
            Direction = [sp.Direction.X, sp.Direction.Y, sp.Direction.Z],
            InnerCutoff = sp.InnerCutoff,
            OuterCutoff = sp.OuterCutoff,
            CastsShadow = sp.CastsShadow,
            Range = sp.Range
        },
        PointLight p => new LightDefinition
        {
            Type = "point",
            Enabled = p.Enabled,
            Color = [p.Color.X, p.Color.Y, p.Color.Z],
            Intensity = p.Intensity,
            Constant = p.Constant,
            Linear = p.Linear,
            Quadratic = p.Quadratic
        },
        _ => throw new ArgumentOutOfRangeException(nameof(l), l, "Unknown light type.")
    };
}
