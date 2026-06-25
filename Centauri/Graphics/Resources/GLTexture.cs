namespace Centauri.Graphics.Resources;

using Silk.NET.OpenGL;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using HighDynamicRange;

public class GLTexture : GLResource
{
    // True when the texture holds linear floating-point radiance (loaded from
    // a <c>.hdr</c> / <c>.exr</c> panorama) rather than 8-bit sRGB data.
    // Consumers such as the skybox use this to decide whether to tonemap.
    public bool IsHdr { get; }

    // Loads from disk: .hdr / .exr decode to a linear float panorama, everything
    // else to 8-bit sRGB.
    public GLTexture(GL gl, string path) : base(gl)
    {
        var fullPath = Path.GetFullPath(path);

        Handle = Gl.GenTexture();
        Gl.BindTexture(TextureTarget.Texture2D, Handle);

        if (HDRLoader.IsHDRPath(fullPath))
        {
            LoadHDR(fullPath);
            IsHdr = true;
        }
        else
        {
            LoadLdr(fullPath);
        }

        SetParameters(IsHdr);
    }

    // Wraps an in-memory 8-bit RGBA buffer (e.g. the 1×1 default texture).
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

    private unsafe void LoadLdr(string fullPath)
    {
        using var img = Image.Load<Rgba32>(fullPath);
        img.Mutate(x => x.Flip(FlipMode.Vertical));

        Span<byte> pixels = new byte[img.Width * img.Height * 4];
        img.CopyPixelDataTo(pixels);

        fixed (void* data = pixels)
        {
            Gl.TexImage2D(
                TextureTarget.Texture2D, 0, InternalFormat.Rgba8,
                (uint)img.Width, (uint)img.Height, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, data);
        }
    }

    // Float panorama uploaded as RGB16F so the full dynamic range is kept on the
    // GPU; tonemapping/exposure happens later in the skybox shader.
    private unsafe void LoadHDR(string fullPath)
    {
        var image = HDRLoader.Load(fullPath);

        fixed (void* data = image.Pixels)
        {
            Gl.TexImage2D(
                TextureTarget.Texture2D, 0, InternalFormat.Rgb16f,
                (uint)image.Width, (uint)image.Height, 0,
                PixelFormat.Rgb, PixelType.Float, data);
        }
    }

    private void SetParameters(bool hdr)
    {
        // Horizontal axis always wraps (the 360° seam); equirect panoramas clamp the
        // vertical axis so the poles don't bleed. HDR samples mip 0 only (no chain);
        // LDR gets a trilinear mip chain.
        GLSampler.Set(Gl, TextureTarget.Texture2D,
            wrapS: GLEnum.Repeat,
            wrapT: hdr ? GLEnum.ClampToEdge : GLEnum.Repeat,
            minFilter: hdr ? GLEnum.Linear : GLEnum.LinearMipmapLinear,
            magFilter: GLEnum.Linear);

        if (!hdr)
        {
            Gl.GenerateMipmap(TextureTarget.Texture2D);
            
            Gl.GetFloat((GLEnum)0x84FF, out float maxAniso); 
            Gl.TexParameter(TextureTarget.Texture2D, (TextureParameterName)0x84FE,
                Math.Max(1f, Math.Min(8f, maxAniso))); 
        }
    }

    protected override void DeleteGL() => Gl.DeleteTexture(Handle);
}