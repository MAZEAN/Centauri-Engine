namespace Centauri.Graphics.Resources.Materials;

using System.Numerics;

public class Material
{
    public string     Name      { get; set; } = null!;
    public GLShader   Shader    { get; set; }
    public GLTexture? Albedo    { get; set; } // base color
    public GLTexture? Normal    { get; set; } // normal map
    public GLTexture? Roughness { get; set; } // roughness map
    public GLTexture? Metallic  { get; set; } // metallic map
    public GLTexture? AO        { get; set; } // ambient occlusion
    public GLTexture? Height    { get; set; } // parallax occlusion mapping — see shaderPBR.frag's ParallaxUV

    // Editable properties
    public Vector4 Color        { get; set; } = Vector4.One;
    public float RoughnessScalar { get; set; } = 0.5f;
    public float MetallicScalar  { get; set; } = 0.1f;
    public float Translucency   { get; set; } = 0f;

    public bool TwoSided { get; set; } = false;
    public bool Wind { get; set; } = false;

    // World-space tri-planar projection instead of stored mesh UVs — for organic/branching
    // geometry (tree bark, rock) where a clean per-vertex unwrap isn't practical.
    public bool  Triplanar      { get; set; } = false;
    public float TriplanarScale { get; set; } = 1f; // world meters spanned by one texture tile

    // How far the parallax offset can push into the surface, in UV units — only meaningful
    // when Height is bound. Small (0.02-0.08 is typical); too large breaks the steep-parallax
    // ray march's step assumption and produces swimming/peeling artifacts.
    public float ParallaxScale { get; set; } = 0.05f;

    // Lets displacement be turned off per entity (via MakeMaterialUnique cloning the same way
    // Color/Roughness/etc. already do) without unbinding Height from the .mat file — useful to
    // A/B a specific instance, or to opt an entity out of a shared material's displacement
    // where it's a known-bad case (e.g. a UV-pole mesh) without affecting every other entity
    // using that material.
    public bool ParallaxEnabled { get; set; } = true;

    public Material(GLShader shader)
    {
        Shader = shader;
    }
    
    public Material Clone() => new(Shader)
    {
        Name = Name,
        Albedo    = Albedo,
        Normal    = Normal,
        Roughness = Roughness,
        Metallic  = Metallic,
        AO        = AO,
        Height    = Height,
        Color          = Color,
        RoughnessScalar = RoughnessScalar,
        MetallicScalar  = MetallicScalar,
        Translucency   = Translucency,
        TwoSided       = TwoSided,
        Wind           = Wind,
        Triplanar      = Triplanar,
        TriplanarScale = TriplanarScale,
        ParallaxScale  = ParallaxScale,
        ParallaxEnabled = ParallaxEnabled
    };
    
    public ulong SortKey
    {
        get
        {
            ulong key = 0;
            
            // | Metallic | Roughness | Normal | Albedo |
            // | 16 bits  | 16 bits   | 16 bits| 16 bits|
            key |= ((ulong)(Albedo?.Handle ?? 0) & 0xFFFF) <<  0;
            key |= ((ulong)(Normal?.Handle ?? 0) & 0xFFFF) << 16;
            key |= ((ulong)(Roughness?.Handle ?? 0) & 0xFFFF) << 32;
            key |= ((ulong)(Metallic?.Handle ?? 0) & 0xFFFF) << 48;
            
            return key;
        }
    }
}