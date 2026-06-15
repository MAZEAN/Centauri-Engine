namespace Centauri.Graphics.Resources;

using Silk.NET.OpenGL;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using HighDynamicRange;

public class GLTexture : IDisposable
{
    private readonly GL _gl;
    public uint Handle { get; }
    
    public bool IsHdr { get; }
    
    // True when the texture holds linear floating-point radiance (loaded from
    // a <c>.hdr</c> / <c>.exr</c> panorama) rather than 8-bit sRGB data.
    // Consumers such as the skybox use this to decide whether to tonemap.
    public GLTexture(GL gl, string path)
    {
        _gl = gl;
        var fullPath = Path.GetFullPath(path);

        Handle = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, Handle);

        if (HdrLoader.IsHdrPath(fullPath))
        {
            LoadHdr(fullPath);
            IsHdr = true;
        }
        else
        {
            LoadLdr(fullPath);
        }

        SetParameters();
    }

    private unsafe void LoadLdr(string fullPath)
    {
        using var img = Image.Load<Rgba32>(fullPath);

        img.Mutate(x => x.Flip(FlipMode.Vertical));

        Span<byte> pixels = new byte[img.Width * img.Height * 4];
        img.CopyPixelDataTo(pixels);

        fixed (void* data = pixels)
        {
            _gl.TexImage2D(
                TextureTarget.Texture2D,
                0,
                InternalFormat.Rgba8,
                (uint)img.Width,
                (uint)img.Height,
                0,
                PixelFormat.Rgba,
                PixelType.UnsignedByte,
                data
            );
        }
    }

    // Float panorama uploaded as RGB16F so the full dynamic range is kept on
    // the GPU; tonemapping/exposure happens later in the skybox shader.
    private unsafe void LoadHdr(string fullPath)
    {
        var image = HdrLoader.Load(fullPath);

        fixed (void* data = image.Pixels)
        {
            _gl.TexImage2D(
                TextureTarget.Texture2D,
                0,
                InternalFormat.Rgb16f,
                (uint)image.Width,
                (uint)image.Height,
                0,
                PixelFormat.Rgb,
                PixelType.Float,
                data
            );
        }
    }

    public unsafe GLTexture(GL gl, Span<byte> data, uint width, uint height)
    {
        _gl = gl;
        Handle = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, Handle);

        fixed (void* d = &data[0])
        {
            _gl.TexImage2D(
                TextureTarget.Texture2D,
                0,
                InternalFormat.Rgba8,
                width,
                height,
                0,
                PixelFormat.Rgba,
                PixelType.UnsignedByte,
                d
            );
        }

        SetParameters();
    }

    private void SetParameters()
    {
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS,     (int)GLEnum.Repeat);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT,     (int)GLEnum.Repeat);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.LinearMipmapLinear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.GenerateMipmap(TextureTarget.Texture2D);
    }

    public void Dispose()
    {
        _gl.DeleteTexture(Handle);
    }
}