namespace Centauri.Rendering.Postprocessing;

using Silk.NET.OpenGL;

using Graphics.Resources;
using Utils.Misc;
using Config;
using World;
using TAA;

public sealed class PostProcessor : IDisposable
{
    private readonly GL _gl;
    private readonly HDRFramebuffer _hdr;
    private readonly AppConfig _config;
    private readonly TAAPass _taa;
    
    private uint _width, _height;
    
    private readonly GLShader _tonemap;
    private readonly uint _emptyVao;   // core profile needs a bound VAO for attribute-less draws
    private readonly BloomPass _bloom;

    public PostProcessor(GL gl, HDRFramebuffer hdr, AppConfig config, uint width, uint height)
    {
        _gl = gl;
        _hdr = hdr;
        _config = config;
        _width = width;
        _height = height;
        
        _tonemap = new GLShader(gl,
            PathResolver.Resolve("Assets/Shaders/Post/post.vert"),
            PathResolver.Resolve("Assets/Shaders/Post/post.frag"));
        _bloom = new BloomPass(gl, _config.Bloom, width, height);
        _taa = new TAAPass(gl, _config.TAA, width, height);
        _emptyVao = gl.GenVertexArray();
    }

    public void Resize(uint width, uint height)
    {
        _width = width;
        _height = height;
        _hdr.Resize(width, height);
        _bloom.Resize(width, height);
        _taa.Resize(width, height);
    }

    public void BeginScene() => _hdr.Bind();
    
    public System.Numerics.Vector2 NextTaaJitter() => _taa.NextJitter(_width, _height);
    public uint VelocityTexture => _taa.VelocityTexture;   // TAA motion vectors, for the debug view

    public void Composite(Camera camera, uint depthTex, bool taaAvailable)
    {
        _hdr.Resolve();
        
        var sceneColor = _hdr.ResolvedTexture;
        
        var taaActive = taaAvailable && _config.TAA.Enabled;
        if (taaActive)
        {
            _taa.Render(sceneColor, depthTex, camera);
            sceneColor = _taa.OutputTexture;
        }
        
        var bloomActive = _config.Bloom.Enabled;
        if (bloomActive)
            _bloom.Render(sceneColor);
        
        // back to the screen for the tonemap (bloom left its own mip FBOs bound)
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.Viewport(0, 0, _width, _height);
        
        SetRenderState();

        _tonemap.Use();
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, sceneColor);
        
        _tonemap.SetUniform("uHdr",        0);
        _tonemap.SetUniform("uExposure",   _config.ColorGrading.Exposure);
        _tonemap.SetUniform("uBlackLevel", _config.ColorGrading.BlackLevel);
        _tonemap.SetUniform("uContrast",   _config.ColorGrading.Contrast);
        _tonemap.SetUniform("uSaturation", _config.ColorGrading.Saturation);
        
        // Bloom
        _gl.ActiveTexture(TextureUnit.Texture1);
        _gl.BindTexture(TextureTarget.Texture2D, bloomActive ? _bloom.BloomTexture : 0);
        
        _tonemap.SetUniform("uBloom",          1);
        _tonemap.SetUniform("uHasBloom",       bloomActive ? 1 : 0);
        _tonemap.SetUniform("uBloomIntensity", _config.Bloom.Intensity);
        
        _gl.BindVertexArray(_emptyVao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        _gl.BindVertexArray(0);

        ResetRenderState();
    }

    private void SetRenderState()
    {
        _gl.Disable(EnableCap.DepthTest);
    }
    
    private void ResetRenderState()
    {
        _gl.Enable(EnableCap.DepthTest);
    }

    public void Dispose()
    {
        _hdr.Dispose();
        _tonemap.Dispose();
        _bloom.Dispose();
        _taa.Dispose();
        
        _gl.DeleteVertexArray(_emptyVao);
    }
}