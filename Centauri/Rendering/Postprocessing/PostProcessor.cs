namespace Centauri.Rendering.Postprocessing;

using Silk.NET.OpenGL;

using Graphics.Resources;
using Utils.Misc;
using Config;
using World;
using SSR;

public sealed class PostProcessor : IDisposable
{
    private readonly GL _gl;
    private readonly HDRFramebuffer _hdr;
    private readonly ColorGrading _grading;
    private readonly BloomConfig _bloomConfig;
    private readonly SSRConfig _ssrConfig;
    private readonly SSRPass _ssr;
    private uint _width, _height;
    
    private readonly GLShader _tonemap;
    private readonly uint _emptyVao;   // core profile needs a bound VAO for attribute-less draws
    private readonly BloomPass _bloom;

    public PostProcessor(GL gl, HDRFramebuffer hdr, ColorGrading grading, BloomConfig bloomConfig, SSRConfig ssrConfig, uint width, uint height)
    {
        _gl = gl;
        _hdr = hdr;
        _grading = grading;
        _bloomConfig = bloomConfig;
        _ssrConfig = ssrConfig;
        _width = width;
        _height = height;
        
        _tonemap = new GLShader(gl,
            PathResolver.Resolve("Assets/Shaders/Post/post.vert"),
            PathResolver.Resolve("Assets/Shaders/Post/post.frag"));
        _bloom = new BloomPass(gl, bloomConfig, width, height);
        _ssr = new SSRPass(gl, ssrConfig, width, height);
        _emptyVao = gl.GenVertexArray();
    }

    public void Resize(uint width, uint height)
    {
        _width = width;
        _height = height;
        _hdr.Resize(width, height);
        _bloom.Resize(width, height);
        _ssr.Resize(width, height);
    }

    public void BeginScene() => _hdr.Bind();

    public void Composite(Camera camera, uint depthTex, uint normalTex, uint materialTex, bool ssrAvailable)
    {
        _hdr.Resolve();
        
        var ssrActive = ssrAvailable && _ssrConfig.Enabled;
        if (ssrActive)
            _ssr.Render(_hdr.ResolvedTexture, depthTex, normalTex, materialTex, camera);
        
        var bloomActive = _bloomConfig.Enabled;
        if (bloomActive)
            _bloom.Render(_hdr.ResolvedTexture);

        // back to the screen for the tonemap (bloom left its own mip FBOs bound)
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.Viewport(0, 0, _width, _height);
        
        _gl.Disable(EnableCap.DepthTest);

        _tonemap.Use();
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _hdr.ResolvedTexture);
        
        _tonemap.SetUniform("uHdr",        0);
        _tonemap.SetUniform("uExposure",   _grading.Exposure);
        _tonemap.SetUniform("uBlackLevel", _grading.BlackLevel);
        _tonemap.SetUniform("uContrast",   _grading.Contrast);
        _tonemap.SetUniform("uSaturation", _grading.Saturation);
        
        // Bloom
        _gl.ActiveTexture(TextureUnit.Texture1);
        _gl.BindTexture(TextureTarget.Texture2D, bloomActive ? _bloom.BloomTexture : 0);
        
        _tonemap.SetUniform("uBloom",          1);
        _tonemap.SetUniform("uHasBloom",       bloomActive ? 1 : 0);
        _tonemap.SetUniform("uBloomIntensity", _bloomConfig.Intensity);
        
        _gl.ActiveTexture(TextureUnit.Texture2);
        _gl.BindTexture(TextureTarget.Texture2D, ssrActive ? _ssr.ReflectionTexture : 0);
        
        _tonemap.SetUniform("uSsr",    2);
        _tonemap.SetUniform("uHasSsr", ssrActive ? 1 : 0);

        _gl.BindVertexArray(_emptyVao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        _gl.BindVertexArray(0);

        _gl.Enable(EnableCap.DepthTest);
    }

    public void Dispose()
    {
        _hdr.Dispose();
        _tonemap.Dispose();
        _bloom.Dispose();
        _ssr.Dispose();
        _gl.DeleteVertexArray(_emptyVao);
    }
}