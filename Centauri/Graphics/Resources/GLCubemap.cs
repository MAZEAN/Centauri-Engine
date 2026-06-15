namespace Centauri.Graphics.Resources;

using Silk.NET.OpenGL;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

public class GLCubemap : IDisposable
{
    private readonly GL _gl;
    public uint Handle { get; }

    // 6 face images in GL order: +X, -X, +Y, -Y, +Z, -Z
    private GLCubemap(GL gl, IReadOnlyList<Image<Rgba32>> faces)
    {
        if (faces.Count != 6)
            throw new ArgumentException("A cubemap needs exactly 6 faces.", nameof(faces));

        _gl = gl;
        Handle = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.TextureCubeMap, Handle);

        for (var i = 0; i < 6; i++)
            Upload(i, faces[i]);

        Configure();
    }

    private GLCubemap(GL gl, uint handle)
    {
        _gl = gl;
        Handle = handle;
    }

    public static GLCubemap CreateEmpty(GL gl, int size)
    {
        var handle = gl.GenTexture();
        gl.BindTexture(TextureTarget.TextureCubeMap, handle);

        unsafe
        {
            for (var i = 0; i < 6; i++)
                gl.TexImage2D(TextureTarget.TextureCubeMapPositiveX + i, 0,
                    InternalFormat.Rgba8, (uint)size, (uint)size, 0,
                    PixelFormat.Rgba, PixelType.UnsignedByte, null);
        }

        gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMinFilter, (int)GLEnum.LinearMipmapLinear);
        gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapS,     (int)GLEnum.ClampToEdge);
        gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapT,     (int)GLEnum.ClampToEdge);
        gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapR,     (int)GLEnum.ClampToEdge);

        return new GLCubemap(gl, handle);
    }

    public void GenerateMipmaps()
    {
        _gl.BindTexture(TextureTarget.TextureCubeMap, Handle);
        _gl.GenerateMipmap(TextureTarget.TextureCubeMap);
    }

    private unsafe void Upload(int index, Image<Rgba32> img)
    {
        Span<byte> pixels = new byte[img.Width * img.Height * 4];
        img.CopyPixelDataTo(pixels);
        fixed (void* data = pixels)
        {
            _gl.TexImage2D(
                TextureTarget.TextureCubeMapPositiveX + index,
                0, InternalFormat.Rgba8,
                (uint)img.Width, (uint)img.Height, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, data);
        }
    }

    private void Configure()
    {
        _gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapS,     (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapT,     (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapR,     (int)GLEnum.ClampToEdge);
    }

    // single horizontal-cross image: 4 cells wide x 3 tall, square faces
    public static GLCubemap FromCross(GL gl, string crossPath)
    {
        using var img = Image.Load<Rgba32>(Path.GetFullPath(crossPath));

        var f = img.Width / 4;
        if (img.Width % 4 != 0 || img.Height != f * 3)
            throw new ArgumentException(
                $"'{crossPath}' is not a 4x3 horizontal-cross cubemap ({img.Width}x{img.Height}).");

        //        +Y
        //  -X  +Z  +X  -Z
        //        -Y
        // (col,row) per face in GL order +X,-X,+Y,-Y,+Z,-Z
        (int cx, int cy)[] cells = [ 
            (2, 1), (0, 1),
            (1, 0), (1, 2),
            (1, 1), (3, 1) 
        ];

        var faces = new Image<Rgba32>[6];
        for (var i = 0; i < 6; i++)
        {
            var (cx, cy) = cells[i];
            faces[i] = img.Clone(c => c.Crop(new Rectangle(cx * f, cy * f, f, f)));
        }

        try
        {
            return new GLCubemap(gl, faces);
        }
        finally
        {
            foreach (var im in faces) im.Dispose();
        }
    }

    public void Bind(TextureUnit unit)
    {
        _gl.ActiveTexture(unit);
        _gl.BindTexture(TextureTarget.TextureCubeMap, Handle);
    }

    public void Dispose() => _gl.DeleteTexture(Handle);
}