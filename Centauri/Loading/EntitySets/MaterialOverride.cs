namespace Centauri.Loading;

using System.Numerics;
using System.Text.Json.Serialization;

using Graphics.Resources.Materials;

// A live snapshot of one mesh slot's per-entity material property edits (EntityMaterialSection —
// Color/Roughness/Metallic/Translucency/UV/TwoSided/Wind/Triplanar/TriplanarScale/ParallaxScale/
// ParallaxEnabled), captured once that slot's Material has been cloned via
// Entity.MakeMaterialUnique so the edit doesn't leak into every other entity sharing the same
// asset (see Entity.OwnsMaterial). Every field is written in full rather than diffed against the
// shared asset's own defaults — "this slot has an override at all" is already the signal (this
// record's presence in EntityDefinition.MaterialOverrides), matching how Position/Scale/Rotation
// are written in full elsewhere in this schema rather than only-what-changed. Field names mirror
// MaterialDefinition's own (roughnessScalar/metallicScalar/etc.), not Material's C# property
// names, for the same .mat-file-authoring consistency.
public sealed class MaterialOverride
{
    [JsonPropertyName("color")]              public float[] Color              { get; set; } = [1f, 1f, 1f, 1f];
    [JsonPropertyName("roughnessScalar")]    public float   RoughnessScalar    { get; set; } = 0.5f;
    [JsonPropertyName("metallicScalar")]     public float   MetallicScalar     { get; set; } = 0.1f;
    [JsonPropertyName("translucencyScalar")] public float   TranslucencyScalar { get; set; } = 0f;
    [JsonPropertyName("uvScale")]            public float[] UvScale            { get; set; } = [1f, 1f];
    [JsonPropertyName("uvOffset")]           public float[] UvOffset           { get; set; } = [0f, 0f];
    [JsonPropertyName("twoSided")]           public bool    TwoSided           { get; set; }
    [JsonPropertyName("wind")]               public bool    Wind               { get; set; }
    [JsonPropertyName("triplanar")]          public bool    Triplanar          { get; set; }
    [JsonPropertyName("triplanarScale")]     public float   TriplanarScale     { get; set; } = 1f;
    [JsonPropertyName("parallaxScale")]      public float   ParallaxScale      { get; set; } = 0.05f;
    [JsonPropertyName("parallaxEnabled")]    public bool    ParallaxEnabled    { get; set; } = true;

    public static MaterialOverride From(Material m) => new()
    {
        Color              = [m.Color.X, m.Color.Y, m.Color.Z, m.Color.W],
        RoughnessScalar    = m.RoughnessScalar,
        MetallicScalar     = m.MetallicScalar,
        TranslucencyScalar = m.Translucency,
        UvScale            = [m.UvScale.X, m.UvScale.Y],
        UvOffset           = [m.UvOffset.X, m.UvOffset.Y],
        TwoSided           = m.TwoSided,
        Wind               = m.Wind,
        Triplanar          = m.Triplanar,
        TriplanarScale     = m.TriplanarScale,
        ParallaxScale      = m.ParallaxScale,
        ParallaxEnabled    = m.ParallaxEnabled,
    };

    public void ApplyTo(Material m)
    {
        if (Color.Length == 4)
            m.Color = new Vector4(Color[0], Color[1], Color[2], Color[3]);
        m.RoughnessScalar = RoughnessScalar;
        m.MetallicScalar  = MetallicScalar;
        m.Translucency    = TranslucencyScalar;
        if (UvScale.Length == 2)
            m.UvScale = new Vector2(UvScale[0], UvScale[1]);
        if (UvOffset.Length == 2)
            m.UvOffset = new Vector2(UvOffset[0], UvOffset[1]);
        m.TwoSided        = TwoSided;
        m.Wind             = Wind;
        m.Triplanar        = Triplanar;
        m.TriplanarScale   = TriplanarScale;
        m.ParallaxScale    = ParallaxScale;
        m.ParallaxEnabled  = ParallaxEnabled;
    }
}
