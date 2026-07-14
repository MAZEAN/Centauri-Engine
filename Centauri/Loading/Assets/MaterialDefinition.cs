namespace Centauri.Loading;

using System.Text.Json.Serialization;

public class MaterialDefinition
{
    // Registry id — defaults to the filename (sans extension) when omitted; only needed to
    // give a material a name that differs from its file, or to disambiguate files that'd
    // otherwise collide (two "Bark.mat" in different folders). Consumed while building the
    // registry (ResourceSystem), not read off the deserialized object afterward.
    [JsonPropertyName("id")]        public string? Id { get; set; }

    // Another material (by id) to inherit from — shared fields are merged at the raw JSON
    // level before deserialization, so this material only needs to state what differs.
    // Consumed the same way as "id"; never present on the final merged object.
    [JsonPropertyName("extends")]   public string? Extends { get; set; }

    [JsonPropertyName("name")]     public string Name { get; set; } = "Default";
    [JsonPropertyName("shader")]    public string Shader { get; set; } = "";
    [JsonPropertyName("albedo")]    public string? Albedo    { get; set; }
    [JsonPropertyName("normal")]    public string? Normal    { get; set; }
    [JsonPropertyName("roughness")] public string? Roughness { get; set; }
    [JsonPropertyName("metallic")]  public string? Metallic  { get; set; }
    [JsonPropertyName("ao")]        public string? AO        { get; set; }
    [JsonPropertyName("height")]    public string? Height    { get; set; } // parallax occlusion mapping
    [JsonPropertyName("opacity")]   public string? Opacity   { get; set; } // merged into albedo's alpha channel at load

    [JsonPropertyName("roughnessScalar")] public float RoughnessScalar { get; set; } = 0.5f;
    [JsonPropertyName("metallicScalar")]  public float MetallicScalar  { get; set; } = 0.1f;
    [JsonPropertyName("translucencyScalar")] public float TranslucencyScalar { get; set; } = 0f;
    [JsonPropertyName("color")]          public float[] Color        { get; set; } = [1f, 1f, 1f, 1f];
    [JsonPropertyName("twoSided")]       public bool   TwoSided      { get; set; } = false;
    [JsonPropertyName("wind")]           public bool   Wind          { get; set; } = false;
    [JsonPropertyName("triplanar")]      public bool   Triplanar      { get; set; } = false;
    [JsonPropertyName("triplanarScale")] public float  TriplanarScale { get; set; } = 1f;
    [JsonPropertyName("parallaxScale")]  public float  ParallaxScale  { get; set; } = 0.05f;
    [JsonPropertyName("parallaxEnabled")] public bool  ParallaxEnabled { get; set; } = true;
}
