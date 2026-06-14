namespace Centauri.Rendering.Resources;

using Silk.NET.OpenGL;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

public class GLCubemap : IDisposable
{
    private readonly GL _gl;
    public uint Handle { get; }
    
    private static readonly string[] FaceOrder  = ["right", "left", "top", "bottom", "front", "back"];
    private static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".bmp", ".tga"];

    // faces in GL order: +X, -X, +Y, -Y, +Z, -Z
    public unsafe GLCubemap(GL gl, IReadOnlyList<string> facePaths)
    {
        if (facePaths.Count != 6)
            throw new ArgumentException("A cubemap needs exactly 6 face images.", nameof(facePaths));

        _gl = gl;
        Handle = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.TextureCubeMap, Handle);

        for (var i = 0; i < 6; i++)
        {
            using var img = Image.Load<Rgba32>(Path.GetFullPath(facePaths[i]));
            // NB: unlike GLTexture we do NOT flip vertically — cube faces use a top-left origin.

            Span<byte> pixels = new byte[img.Width * img.Height * 4];
            img.CopyPixelDataTo(pixels);

            fixed (void* data = pixels)
            {
                _gl.TexImage2D(
                    TextureTarget.TextureCubeMapPositiveX + i,
                    0, InternalFormat.Rgba8,
                    (uint)img.Width, (uint)img.Height, 0,
                    PixelFormat.Rgba, PixelType.UnsignedByte, data);
            }
        }

        _gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapS,     (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapT,     (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapR,     (int)GLEnum.ClampToEdge);
    }

    // folder with conventional face filenames (right/left/top/bottom/front/back)
    public static GLCubemap FromFolder(GL gl, string folder)
    {
        var ext = Array.Find(Extensions, e => File.Exists(Path.Combine(folder, "right" + e)))
                  ?? throw new FileNotFoundException(
                      $"No skybox faces in '{folder}' (need right/left/top/bottom/front/back).");

        var faces = Array.ConvertAll(FaceOrder, n => Path.Combine(folder, n + ext));  // +X,-X,+Y,-Y,+Z,-Z
        return new GLCubemap(gl, faces);
    }

    public void Bind(TextureUnit unit)
    {
        _gl.ActiveTexture(unit);
        _gl.BindTexture(TextureTarget.TextureCubeMap, Handle);
    }

    public void Dispose() => _gl.DeleteTexture(Handle);
}