namespace SimpleTerrain.Rendering;

using System.Numerics;

public class Material : IDisposable
{
    public GLShader   Shader    { get; set; }
    public GLTexture? Albedo    { get; set; } // base color
    public GLTexture? Normal    { get; set; } // normal map
    public GLTexture? Roughness { get; set; } // roughness map
    public GLTexture? Metallic  { get; set; } // metallic map
    public GLTexture? AO        { get; set; } // ambient occlusion

    public Vector4 Color     { get; set; } = Vector4.One;
    public Vector2 UvScale   { get; set; } = Vector2.One;
    public Vector2 UvOffset  { get; set; } = Vector2.Zero;

    // fallback scalar values when maps are not provided
    public float RoughnessValue { get; set; } = 0.5f;
    public float MetallicValue  { get; set; } = 0.0f;

    public Material(GLShader shader)
    {
        Shader = shader;
    }

    public void Dispose()
    {
        Shader.Dispose();
        Albedo?.Dispose();
        Normal?.Dispose();
        Roughness?.Dispose();
        Metallic?.Dispose();
        AO?.Dispose();
    }
}