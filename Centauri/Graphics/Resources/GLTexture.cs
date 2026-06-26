namespace Centauri.Graphics.Resources;

using Silk.NET.OpenGL;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using HighDynamicRange;

public sealed class TextureData
{
    public bool    IsHdr;
    public int     Width;
    public int     Height;
    
    public byte[]?  Ldr;   // RGBA8, vertically flipped
    public float[]? Hdr;   // RGB float
}

public class GLTexture : GLResource
{
    private const GLEnum               MaxSupportedAniso = (GLEnum)0x84FF;
    private const TextureParameterName TextureMaxAniso   = (TextureParameterName)0x84FE;
    private const float                AnisoRequest      = 8f;
    
    private static readonly List<GLTexture> Anisotropic = new();
    private static bool _anisoEnabled = true;

    private float _maxAniso = 1f;   // driver-clamped per-texture ceiling (>= 1)
    public bool IsHdr { get; }
    
    public unsafe GLTexture(GL gl, TextureData data) : base(gl)
    {
        Handle = Gl.GenTexture();
        Gl.BindTexture(TextureTarget.Texture2D, Handle);

        if (data.IsHdr)
        {
            IsHdr = true;
            fixed (float* p = data.Hdr)
                Gl.TexImage2D(
                    TextureTarget.Texture2D, 0, InternalFormat.Rgb16f,
                    (uint)data.Width, (uint)data.Height, 0,
                    PixelFormat.Rgb, PixelType.Float, p);
        }
        else
        {
            fixed (byte* p = data.Ldr)
                Gl.TexImage2D(
                    TextureTarget.Texture2D, 0, InternalFormat.Rgba8,
                    (uint)data.Width, (uint)data.Height, 0,
                    PixelFormat.Rgba, PixelType.UnsignedByte, p);
        }

        SetParameters(IsHdr);
    }
    
    public GLTexture(GL gl, string path) : this(gl, Decode(path)) { }
    
    public static TextureData Decode(string path)
    {
        var fullPath = Path.GetFullPath(path);

        if (HDRLoader.IsHDRPath(fullPath))
        {
            var img = HDRLoader.Load(fullPath);
            return new TextureData { IsHdr = true, Width = img.Width, Height = img.Height, Hdr = img.Pixels };
        }

        using var image = Image.Load<Rgba32>(fullPath);
        image.Mutate(x => x.Flip(FlipMode.Vertical));

        var pixels = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(pixels);
        return new TextureData { IsHdr = false, Width = image.Width, Height = image.Height, Ldr = pixels };
    }

    public unsafe GLTexture(GL gl, Span<byte> data, uint width, uint height) : base(gl)
    {
        Handle = Gl.GenTexture();
        Gl.BindTexture(TextureTarget.Texture2D, Handle);

        fixed (void* d = &data[0])
        {
            Gl.TexImage2D(
                TextureTarget.Texture2D, 0, InternalFormat.Rgba8,
                width, height, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, d);
        }

        SetParameters(hdr: false);
    }

    private void SetParameters(bool hdr)
    {
        GLSampler.Set(Gl, TextureTarget.Texture2D,
            wrapS: GLEnum.Repeat,
            wrapT: hdr ? GLEnum.ClampToEdge : GLEnum.Repeat,
            minFilter: hdr ? GLEnum.Linear : GLEnum.LinearMipmapLinear,
            magFilter: GLEnum.Linear
        );

        if (!hdr)
        {
            Gl.GenerateMipmap(TextureTarget.Texture2D);
            
            Gl.GetFloat(MaxSupportedAniso, out float maxAniso);
            _maxAniso = Math.Max(1f, Math.Min(AnisoRequest, maxAniso));   // 1 = off, if unsupported
            
            Gl.TexParameter(TextureTarget.Texture2D, TextureMaxAniso, _anisoEnabled ? _maxAniso : 1f);
            Anisotropic.Add(this);
        }
    }

    public static void SetAnisotropy(GL gl, bool enabled)
    {
        if (enabled == _anisoEnabled) return;
        _anisoEnabled = enabled;

        foreach (var tex in Anisotropic)
        {
            gl.BindTexture(TextureTarget.Texture2D, tex.Handle);
            gl.TexParameter(TextureTarget.Texture2D, TextureMaxAniso, enabled ? tex._maxAniso : 1f);
        }
    }

    protected override void DeleteGL()
    {
        Anisotropic.Remove(this);
        Gl.DeleteTexture(Handle);
    }
}