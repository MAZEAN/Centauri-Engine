namespace Centauri.Graphics.Resources;

using System.Numerics;

public class Material
{
    public GLShader   Shader    { get; set; }
    public GLTexture? Albedo    { get; set; } // base color
    public GLTexture? Normal    { get; set; } // normal map
    public GLTexture? Roughness { get; set; } // roughness map
    public GLTexture? Metallic  { get; set; } // metallic map
    public GLTexture? AO        { get; set; } // ambient occlusion
    
    // Editable properties
    public Vector4 Color        { get; set; } = Vector4.One;
    public float RoughnessValue { get; set; } = 0.5f;
    public float MetallicValue  { get; set; } = 0.1f;

    public Material(GLShader shader)
    {
        Shader = shader;
    }
    
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