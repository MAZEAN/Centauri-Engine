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
        Color          = Color,
        RoughnessScalar = RoughnessScalar,
        MetallicScalar  = MetallicScalar,
        Translucency   = Translucency,
        TwoSided       = TwoSided,
        Wind           = Wind,
        Triplanar      = Triplanar,
        TriplanarScale = TriplanarScale
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