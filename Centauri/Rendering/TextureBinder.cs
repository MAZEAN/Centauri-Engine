namespace Centauri.Rendering;

using Silk.NET.OpenGL;

using Graphics.Resources;

// Per-frame texture-unit bind cache: skips a redundant glBindTexture when a unit already
// holds the wanted handle. Covers the material slots (0-4); returns the count of real GPU
// binds for stats. Reset() must run each frame since other passes bind units directly.
public sealed class TextureBinder
{
    private readonly GL _gl;
    private readonly uint[] _bound;

    public TextureBinder(GL gl)
    {
        _gl = gl;
        _gl.GetInteger(GLEnum.MaxTextureImageUnits, out var maxUnits);
        _bound = new uint[maxUnits];
        Array.Fill(_bound, uint.MaxValue);
    }

    public void Reset() => Array.Fill(_bound, uint.MaxValue);

    public int BindMaterial(Material mat)
    {
        var binds = 0;
        binds += Bind(mat.Albedo,    TextureUnit.Texture0);
        binds += Bind(mat.Normal,    TextureUnit.Texture1);
        binds += Bind(mat.Roughness, TextureUnit.Texture2);
        binds += Bind(mat.Metallic,  TextureUnit.Texture3);
        binds += Bind(mat.AO,        TextureUnit.Texture4);
        return binds;
    }

    public int Bind(GLTexture? tex, TextureUnit slot)
    {
        var index  = (int)slot - (int)TextureUnit.Texture0;
        var handle = tex?.Handle ?? 0;

        if (index < 0 || index >= _bound.Length)
            throw new Exception($"Texture slot {slot} exceeds supported range.");

        if (_bound[index] == handle)
            return 0; // cache hit — no GPU bind

        _gl.ActiveTexture(slot);
        _gl.BindTexture(TextureTarget.Texture2D, handle);
        _bound[index] = handle;
        return 1;
    }
}