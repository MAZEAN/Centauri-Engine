namespace Centauri.Loading;

using System.Text.Json.Serialization;

public class EntityDefinition
{
    [JsonPropertyName("name")]     public string  Name     { get; set; } = "Entity";
    [JsonPropertyName("model")]    public string? Model    { get; set; }
    [JsonPropertyName("material")] public string? Material { get; set; }
    [JsonPropertyName("materials")] public MaterialBinding? Materials { get; set; }
    [JsonPropertyName("position")] public float[] Position { get; set; } = [0f, 0f, 0f];
    [JsonPropertyName("scale")]    public float[] Scale    { get; set; } = [1f, 1f, 1f];
    [JsonPropertyName("rotation")] public float[]? Rotation { get; set; }
    [JsonPropertyName("uvScale")]  public float[] UvScale  { get; set; } = [1f, 1f];
    [JsonPropertyName("uvOffset")] public float[] UvOffset { get; set; } = [0f, 0f];
    [JsonPropertyName("enabled")]  public bool    Enabled  { get; set; } = true;

    [JsonPropertyName("light")]    public LightDefinition? Light { get; set; }
    [JsonPropertyName("components")] public List<ComponentDefinition>? Components { get; set; }

    // Parent entity's Name, resolved against every other entity in the *same file* only — see
    // EntitySetLoader.WireHierarchy for why cross-file parenting isn't supported (load order
    // between files isn't guaranteed the way order within one file is). Position/Scale/Rotation
    // above are still this entity's own *local* transform once parented — Parent doesn't adjust
    // them to compensate, so author a child's local transform already relative to its parent
    // (raw re-parent, no world-position-preserving compensation — see
    // Docs/Documentation/TransformHierarchy.md).
    [JsonPropertyName("parent")]   public string? Parent { get; set; }

    // Overrides the resolved material(s)' own triplanar setting for this entity specifically —
    // applies uniformly across all of this entity's meshes (there's no per-mesh override; a
    // material's own "triplanar"/"triplanarScale" already differ per mesh via which material
    // gets assigned to which mesh, which covers the common case — bark triplanar, leaves not —
    // without needing this at all). Rare: only matters when you want a *specific placement* of
    // a model to deviate from what its assigned material(s) normally do.
    [JsonPropertyName("triplanar")]      public bool?  TriplanarOverride      { get; set; }
    [JsonPropertyName("triplanarScale")] public float? TriplanarScaleOverride { get; set; }
}