namespace Centauri.Rendering.Resources;

using Silk.NET.OpenGL;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

public class GLTexture : IDisposable
{
    private readonly GL _gl;
    private readonly uint _handle;

    public uint Handle => _handle;
    public string Path { get; }

    public unsafe GLTexture(GL gl, string path)
    {
        _gl = gl;
        Path = System.IO.Path.GetFullPath(path);

        _handle = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _handle);

        using var img = Image.Load<Rgba32>(Path);

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

        SetParameters();
    }

    public unsafe GLTexture(GL gl, Span<byte> data, uint width, uint height)
    {
        _gl = gl;

        _handle = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _handle);

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
        _gl.DeleteTexture(_handle);
    }
}