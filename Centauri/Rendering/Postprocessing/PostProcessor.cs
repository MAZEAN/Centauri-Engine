namespace Centauri.Rendering.Postprocessing;

using Silk.NET.OpenGL;
using System.Numerics;

using Graphics.Resources;
using Utils.Misc;
using Config;
using World;
using Reflections.SSR;
using TAA;

// The three prepass outputs every post-process pass reads from (depth + view-space normal + roughness/metallic).
public readonly record struct GBufferTextures (
    uint Depth,
    uint Normal,
    uint Material
);

public readonly record struct IblResolveInputs (
    uint  PrefilterMap,
    uint  BrdfLut,
    float MaxReflectionLod,
    float Intensity,
    bool  HasIbl,
    uint  ProbePrefilterMap,
    float ProbeMaxReflectionLod,
    float ProbeIntensity,
    bool  HasProbe,
    Vector3 ProbePosition,   // capture point (cubemap sampling center)
    Vector3 ProbeBoxMin,     // parallax box, world space
    Vector3 ProbeBoxMax,
    float ProbeBoxFalloff
);

public readonly record struct PlanarResolveInputs (
    uint  Map,
    bool  Has,
    float Height,
    float Intensity,
    float Distortion,
    float Blur
);

public sealed class PostProcessor : IDisposable
{
    private readonly GL _gl;
    private readonly HDRFramebuffer _hdr;
    private readonly AppConfig _config;
    private uint _width, _height;
    
    private readonly SSRPass _ssr;
    private readonly TAAPass _taa;
    
    private readonly GLShader _tonemap;
    private readonly BloomPass _bloom;
    private readonly AutoExposurePass _autoExposure;
    private readonly uint _emptyVao;   // core profile needs a bound VAO for attribute-less draws

    public PostProcessor(GL gl, HDRFramebuffer hdr, AppConfig config, uint width, uint height)
    {
        _gl = gl;
        _hdr = hdr;
        _config = config;
        _width = width;
        _height = height;
        
        _tonemap = new GLShader(gl,
            PathResolver.Resolve("Shaders/Post/post.vert"),
            PathResolver.Resolve("Shaders/Post/post.frag"));
        _bloom = new BloomPass(gl, _config.Bloom, width, height);
        _autoExposure = new AutoExposurePass(gl, _config.AutoExposure, width, height);
        _ssr = new SSRPass(gl, _config.SSR, width, height);
        _taa = new TAAPass(gl, _config.TAA, width, height);
        _emptyVao = gl.GenVertexArray();
    }

    public void Resize(uint width, uint height)
    {
        _width = width;
        _height = height;
        _hdr.Resize(width, height);
        _bloom.Resize(width, height);
        _autoExposure.Resize(width, height);
        _ssr.Resize(width, height);
        _taa.Resize(width, height);
    }

    public void BeginScene() => _hdr.Bind();
    
    public Vector2 NextTaaJitter() => _taa.NextJitter(_width, _height);
    public uint VelocityTexture => _taa.VelocityTexture;   // TAA motion vectors, for the debug view

    public void Composite(Camera camera, in GBufferTextures gBuffer,
        bool ssrAvailable, bool taaAvailable, in IblResolveInputs ibl, uint ssaoTex, bool ssaoActive,
        in PlanarResolveInputs planar, float deltaTime)
    {
        _hdr.Resolve();

        var sceneColor = _hdr.ResolvedTexture;

        var ssrActive = ssrAvailable && _config.SSR.Enabled;
        if (ssrActive)
            _ssr.Render(sceneColor, in gBuffer, camera, in ibl, ssaoTex, ssaoActive, in planar);

        var taaActive = taaAvailable && _config.TAA.Enabled;
        var ssrInTonemap = ssrActive && !taaActive;
        if (taaActive)
        {
            _taa.Render(sceneColor, ssrActive ? _ssr.ReflectionTexture : 0, ssrActive, gBuffer.Depth, camera);
            sceneColor = _taa.OutputTexture;
        }
        
        var autoExposureActive = _config.AutoExposure.Enabled;
        if (autoExposureActive)
            _autoExposure.Render(sceneColor, deltaTime);
        
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
        
        _gl.ActiveTexture(TextureUnit.Texture2);
        _gl.BindTexture(TextureTarget.Texture2D, ssrInTonemap ? _ssr.ReflectionTexture : 0);
        
        _tonemap.SetUniform("uSsr",    2);
        _tonemap.SetUniform("uHasSsr", ssrInTonemap ? 1 : 0);
        
        _gl.ActiveTexture(TextureUnit.Texture3);
        _gl.BindTexture(TextureTarget.Texture2D, autoExposureActive ? _autoExposure.AdaptedLuminanceTexture : 0);

        _tonemap.SetUniform("uAutoLuminance",       3);
        _tonemap.SetUniform("uAutoExposureEnabled", autoExposureActive ? 1 : 0);
        _tonemap.SetUniform("uAutoKeyValue",        _config.AutoExposure.KeyValue);
        _tonemap.SetUniform("uAutoMinExposure",     _config.AutoExposure.MinExposure);
        _tonemap.SetUniform("uAutoMaxExposure",     _config.AutoExposure.MaxExposure);
        
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
        _autoExposure.Dispose();
        _ssr.Dispose();
        _taa.Dispose();
        
        _gl.DeleteVertexArray(_emptyVao);
    }
}