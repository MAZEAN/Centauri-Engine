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
    
    private static readonly List<GLTexture> Anisotropic = [];
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
        PremultiplyAlpha(pixels);

        return new TextureData { IsHdr = false, Width = image.Width, Height = image.Height, Ldr = pixels };
    }
    
    // Merges a standalone greyscale opacity map into the albedo's alpha channel at load time —
    // most PBR/foliage texture packs ship opacity as its own file, and the cutout test
    // (shaderPBR.frag, ZPrepass, ShadowMapper) reads alpha straight off the albedo texture, so
    // this is where that gets baked in instead of requiring an offline compositing step.
    public static TextureData DecodeWithOpacity(string albedoPath, string opacityPath)
    {
        using var albedo  = Image.Load<Rgba32>(Path.GetFullPath(albedoPath));
        using var opacity = Image.Load<Rgba32>(Path.GetFullPath(opacityPath));

        if (opacity.Size != albedo.Size)
            opacity.Mutate(x => x.Resize(albedo.Size));

        albedo.Mutate(x => x.Flip(FlipMode.Vertical));
        opacity.Mutate(x => x.Flip(FlipMode.Vertical));

        var pixels = new byte[albedo.Width * albedo.Height * 4];
        albedo.CopyPixelDataTo(pixels);

        var opacityPixels = new byte[opacity.Width * opacity.Height * 4];
        opacity.CopyPixelDataTo(opacityPixels);

        for (var i = 0; i < pixels.Length; i += 4)
            pixels[i + 3] = opacityPixels[i];   // opacity map's red channel -> albedo's alpha

        PremultiplyAlpha(pixels);

        return new TextureData { IsHdr = false, Width = albedo.Width, Height = albedo.Height, Ldr = pixels };
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
    
    // Mipmap generation and bilinear/anisotropic filtering blend neighboring texels' RGB
    // independent of alpha (not alpha-weighted). For an alpha-tested cutout texture (foliage),
    // the "transparent" texels surrounding a leaf shape often carry leftover matte/background
    // color from however the source image was authored/exported, and that color bleeds into
    // the leaf's edge at every mip level and any grazing-angle sample — the classic colored
    // fringe around alpha-tested foliage. Premultiplying means that bleed is toward black (0
    // RGB at 0 alpha) instead of an arbitrary color; shaderPBR.frag un-premultiplies on read so
    // surviving, alpha-tested pixels still get their real, correct color.
    private static void PremultiplyAlpha(byte[] rgba)
    {
        for (var i = 0; i < rgba.Length; i += 4)
        {
            var a = rgba[i + 3];
            if (a == 255) continue;   // fully opaque — no change, and skips the common case fast

            rgba[i]     = (byte)(rgba[i]     * a / 255);
            rgba[i + 1] = (byte)(rgba[i + 1] * a / 255);
            rgba[i + 2] = (byte)(rgba[i + 2] * a / 255);
        }
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