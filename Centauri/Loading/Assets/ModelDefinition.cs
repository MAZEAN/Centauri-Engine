namespace Centauri.Loading;

using System.Text.Json.Serialization;

// A model's own declaration file (Assets/Objects/**/*.model) — the geometry file plus reusable
// defaults for it, the same relationship materials have to their textures. Registered by id
// (filename, or an explicit "id") the same way materials are; "model": "Tree" in an entity
// resolves through this registry exactly like "material": "Bark" does.
public class ModelDefinition
{
    [JsonPropertyName("id")]   public string? Id   { get; set; }
    [JsonPropertyName("path")] public string  Path { get; set; } = "";

    // Default material binding for this model — used whenever an entity places this model
    // without specifying its own "material"/"materials", so the same model doesn't need its
    // material list repeated at every placement. An entity's own binding still wins if given.
    [JsonPropertyName("materials")] public MaterialBinding? Materials { get; set; }

    // Default triplanar override for entities that place this model — see EntityDefinition's
    // TriplanarOverride for why this is rarely needed; exists mainly so a model that's *always*
    // meant to deviate from its material's own setting doesn't need every placement to repeat it.
    [JsonPropertyName("triplanar")]      public bool?  TriplanarOverride      { get; set; }
    [JsonPropertyName("triplanarScale")] public float? TriplanarScaleOverride { get; set; }
}