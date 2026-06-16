namespace Centauri.Rendering;

using Silk.NET.OpenGL;
using Graphics.Resources;
using Utils.Misc;

public sealed class PostProcessor : IDisposable
{
    private readonly GL _gl;
    private readonly HDRFramebuffer _hdr;
    private readonly GLShader _tonemap;
    private readonly uint _emptyVao;   // core profile needs a bound VAO for attribute-less draws

    private readonly ColorGrading _grading;

    public PostProcessor(GL gl, HDRFramebuffer hdr, ColorGrading grading)
    {
        _gl = gl;
        _hdr = hdr;
        _grading = grading;
        
        _tonemap = new GLShader(gl,
            PathResolver.Resolve("Assets/Shaders/Post/post.vert"),
            PathResolver.Resolve("Assets/Shaders/Post/post.frag"));
        _emptyVao = gl.GenVertexArray();
    }

    public void Resize(uint width, uint height) => _hdr.Resize(width, height);

    public void BeginScene() => _hdr.Bind();   // bind HDR target + clear

    public void Composite()
    {
        _hdr.Resolve();                         // → default framebuffer bound
        _gl.Disable(EnableCap.DepthTest);

        _tonemap.Use();
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _hdr.ResolvedTexture);
        _tonemap.SetUniform("uHdr",        0);
        _tonemap.SetUniform("uExposure",   _grading.Exposure);
        _tonemap.SetUniform("uBlackLevel", _grading.BlackLevel);
        _tonemap.SetUniform("uContrast",   _grading.Contrast);
        _tonemap.SetUniform("uSaturation", _grading.Saturation);

        _gl.BindVertexArray(_emptyVao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        _gl.BindVertexArray(0);

        _gl.Enable(EnableCap.DepthTest);
    }

    public void Dispose()
    {
        _hdr.Dispose();
        _tonemap.Dispose();
        _gl.DeleteVertexArray(_emptyVao);
    }
}