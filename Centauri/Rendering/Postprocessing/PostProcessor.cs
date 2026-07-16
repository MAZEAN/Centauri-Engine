namespace Centauri.Rendering.Postprocessing;

using Silk.NET.OpenGL;
using System.Numerics;

using Graphics.Resources;
using Utils.Misc;
using Config;
using World;
using Reflections.SSR;
using TAA;

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
    Vector3 ProbePosition, 
    Vector3 ProbeBoxMin,
    Vector3 ProbeBoxMax,
    float ProbeBoxFalloff
);

public readonly record struct PlanarResolveInputs (
    uint  Map,
    bool  Has,
    float Height,
    float Intensity,
    float Distortion,
    float Blur,
    float MaxRoughness
);

public readonly record struct CompositeRequest(
    Camera Camera,
    GBufferTextures GBuffer,
    bool SsrAvailable,
    bool TaaAvailable,
    IblResolveInputs Ibl,
    uint GtaoTexture,
    bool GtaoActive,
    PlanarResolveInputs Planar,
    float DeltaTime
);

public sealed class PostProcessor : IDisposable
{
    private readonly GL _gl;
    private readonly HDRFramebuffer _hdr;
    private readonly AppConfig _config;
    private readonly Profiling.GPUProfiler _profiler;

    // The scene itself (HDR target, SSR, bloom, autoexposure, TAA) renders at `_width/_height` —
    // AppConfig.Render.RenderScale times the window's actual framebuffer size. The final tonemap
    // draw instead targets `_outputWidth/_outputHeight` (always the window's native size,
    // unscaled): it samples the (possibly smaller) scene color texture through DrawTonemap's
    // linear-filtered sampler into a full native-size viewport, so upscaling to the display falls
    // out of that one draw for free — see RenderConfig.RenderScale.
    private uint _width, _height;
    private uint _outputWidth, _outputHeight;

    private readonly SSRPass _ssr;
    private readonly TAAPass _taa;

    private readonly GLShader _tonemap;
    private readonly BloomPass _bloom;
    private readonly AutoExposurePass _autoExposure;
    private readonly uint _emptyVao;   // core profile needs a bound VAO for attribute-less draws

    public PostProcessor(GL gl, HDRFramebuffer hdr, AppConfig config, Profiling.GPUProfiler profiler,
        uint width, uint height, uint outputWidth, uint outputHeight)
    {
        _gl = gl;
        _hdr = hdr;
        _config = config;
        _profiler = profiler;
        _width = width;
        _height = height;
        _outputWidth = outputWidth;
        _outputHeight = outputHeight;

        _tonemap = new GLShader(gl,
            PathResolver.Resolve("Shaders/Post/post.vert"),
            PathResolver.Resolve("Shaders/Post/post.frag"));

        _bloom = new BloomPass(gl, _config.Bloom, width, height);
        _autoExposure = new AutoExposurePass(gl, _config.AutoExposure, width, height);
        _ssr = new SSRPass(gl, _config.SSR, width, height);
        _taa = new TAAPass(gl, _config.TAA, width, height);

        _emptyVao = gl.GenVertexArray();
    }

    public void Resize(uint width, uint height, uint outputWidth, uint outputHeight)
    {
        _width = width;
        _height = height;
        _outputWidth = outputWidth;
        _outputHeight = outputHeight;
        _hdr.Resize(width, height);
        _bloom.Resize(width, height);
        _autoExposure.Resize(width, height);
        _ssr.Resize(width, height);
        _taa.Resize(width, height);
    }

    public void BeginScene() => _hdr.Bind();
    // See HDRFramebuffer.TryBorrowDepth/ReleaseDepth — lets Forward reuse GeometryPrepass's
    // depth for early-Z instead of RenderingSystem running a second depth-only pass.
    public bool TryBorrowPrepassDepth(uint depthTexture) => _hdr.TryBorrowDepth(depthTexture);
    public void ReleasePrepassDepth() => _hdr.ReleaseDepth();
    public Vector2 NextTaaJitter() => _taa.NextJitter(_width, _height);
    public uint VelocityTexture => _taa.VelocityTexture;   // TAA motion vectors, for the debug view

    // GL_TIME_ELAPSED zones can't nest (see GPUProfiler), so this can't sit inside a single
    // outer "Post" zone the way it used to — instead it opens its own sequential zones,
    // breaking SSR (march + blur + temporal + resolve, the priciest single piece of Composite)
    // out from everything else, which stays bucketed as "Post" same as before.
    public void Composite(in CompositeRequest request)
    {
        using var _ = Profiling.Tracy.Scope("PostProcessor.Composite");

        bool ssrActive, taaActive;
        uint sceneColor;

        using (_profiler.Measure("SSR"))
        {
            using (Profiling.Tracy.Scope("PostProcessor.Composite.HdrResolve"))
                _hdr.Resolve();

            sceneColor = _hdr.ResolvedTexture;

            using var __ = Profiling.Tracy.Scope("PostProcessor.Composite.SSR");
            ssrActive = RenderSsr(request, sceneColor);
        }

        using (_profiler.Measure("Post"))
        {
            using var __ = Profiling.Tracy.Scope("PostProcessor.Composite.Post");

            taaActive = TimedRenderTaa(request, ssrActive, ref sceneColor);
            var ssrInTonemap = ssrActive && !taaActive;

            if (_config.AutoExposure.Enabled)
            {
                using var ___ = Profiling.Tracy.Scope("PostProcessor.Composite.AutoExposure");
                _autoExposure.Render(sceneColor, request.DeltaTime);
            }

            if (_config.Bloom.Enabled)
            {
                using var ___ = Profiling.Tracy.Scope("PostProcessor.Composite.Bloom");
                _bloom.Render(sceneColor);
            }

            using (Profiling.Tracy.Scope("PostProcessor.Composite.Tonemap"))
                DrawTonemap(sceneColor, ssrInTonemap);
        }
    }

    private bool TimedRenderTaa(in CompositeRequest request, bool ssrActive, ref uint sceneColor)
    {
        using var _ = Profiling.Tracy.Scope("PostProcessor.Composite.TAA");
        return RenderTaa(request, ssrActive, ref sceneColor);
    }

    // Returns whether SSR actually ran (request.SsrAvailable && config still enabled).
    private bool RenderSsr(in CompositeRequest request, uint sceneColor)
    {
        var ssrActive = request.SsrAvailable && _config.SSR.Enabled;
        if (ssrActive)
            _ssr.Render(sceneColor, request.GBuffer, request.Camera, request.Ibl,
                request.GtaoTexture, request.GtaoActive, request.Planar);

        return ssrActive;
    }

    // Returns whether TAA actually ran; redirects sceneColor to TAA's output when it did.
    private bool RenderTaa(in CompositeRequest request, bool ssrActive, ref uint sceneColor)
    {
        var taaActive = request.TaaAvailable && _config.TAA.Enabled;
        if (!taaActive) return false;

        _taa.Render(sceneColor, ssrActive ? _ssr.ReflectionTexture : 0, ssrActive,
            request.GBuffer.Depth, request.Camera);
        sceneColor = _taa.OutputTexture;
        return true;
    }

    private void DrawTonemap(uint sceneColor, bool ssrInTonemap)
    {
        // back to the screen for the tonemap (bloom left its own mip FBOs bound). Native output
        // size, not the (possibly smaller) render size sceneColor/bloom/etc. were rendered at —
        // BindScene's linear-filtered sample is what upscales them into this viewport.
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.Viewport(0, 0, _outputWidth, _outputHeight);
        
        SetRenderState();

        _tonemap.Use();
        
        BindScene(sceneColor);
        BindBloom();
        BindSsr(ssrInTonemap);
        BindAutoExposure();

        _gl.BindVertexArray(_emptyVao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        _gl.BindVertexArray(0);

        ResetRenderState();
    }
    
    private void BindScene(uint sceneColor)
    {
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, sceneColor);
        

        _tonemap.SetUniform("uHdr",        0);
        _tonemap.SetUniform("uExposure",   _config.ColorGrading.Exposure);
        _tonemap.SetUniform("uBlackLevel", _config.ColorGrading.BlackLevel);
        _tonemap.SetUniform("uContrast",   _config.ColorGrading.Contrast);
        _tonemap.SetUniform("uSaturation", _config.ColorGrading.Saturation);
    }
    
    private void BindBloom()
    {
        var active = _config.Bloom.Enabled;

        _gl.ActiveTexture(TextureUnit.Texture1);
        _gl.BindTexture(TextureTarget.Texture2D, active ? _bloom.BloomTexture : 0);

        _tonemap.SetUniform("uBloom",          1);
        _tonemap.SetUniform("uHasBloom",       active ? 1 : 0);
        _tonemap.SetUniform("uBloomIntensity", _config.Bloom.Intensity);
        
    }
    
    private void BindSsr(bool active)
    {
        _gl.ActiveTexture(TextureUnit.Texture2);
        _gl.BindTexture(TextureTarget.Texture2D, active ? _ssr.ReflectionTexture : 0);

        _tonemap.SetUniform("uSsr",    2);
        _tonemap.SetUniform("uHasSsr", active ? 1 : 0);
    }

    private void BindAutoExposure()
    {
        var active = _config.AutoExposure.Enabled;

        _gl.ActiveTexture(TextureUnit.Texture3);
        _gl.BindTexture(TextureTarget.Texture2D, active ? _autoExposure.AdaptedLuminanceTexture : 0);

        _tonemap.SetUniform("uAutoLuminance",       3);
        _tonemap.SetUniform("uAutoExposureEnabled", active ? 1 : 0);
        _tonemap.SetUniform("uAutoKeyValue",        _config.AutoExposure.KeyValue);
        _tonemap.SetUniform("uAutoMinExposure",     _config.AutoExposure.MinExposure);
        _tonemap.SetUniform("uAutoMaxExposure",     _config.AutoExposure.MaxExposure);
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