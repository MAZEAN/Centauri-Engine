namespace Centauri.Rendering.Reflections.SSR;

using Silk.NET.OpenGL;
using System.Numerics;

using Config;
using World;
using Graphics.Resources;
using Utils.Misc;
using Targets;
using Postprocessing;

// Screen-space reflections: ray-marches the prepass depth buffer and samples the resolved
// HDR scene to build a reflection term, weighted per-surface by the prepass material buffer
// (roughness/metallic), spatially blurred by roughness, then temporally accumulated (the
// per-frame hit/confidence is unstable on fine or thin geometry — whether the march lands on
// the detail or slips into the gap behind it is sensitive to sub-pixel changes, so without
// smoothing it flickers under camera motion or TAA jitter alone) before being resolved against
// the IBL specular fallback so that, where SSR is confident, it replaces the environment
// reflection the lit pass already applied (rather than stacking on it); where it is not, the
// scene keeps that fallback. The result is the delta the post stack folds into the scene before
// tonemapping.
// See Shaders/SSR/ssr.frag (march), ssr_blur.frag (roughness blur), ssr_temporal.frag
// (accumulation) and ssr_resolve.frag (IBL blend).
public sealed class SSRPass : IDisposable
{
    private const float Tolerance = 0.01f;

    private readonly GL _gl;
    private readonly SSRConfig _config;

    private readonly uint _resDivisor;

    private readonly GLShader _shader;
    private readonly GLShader _blur;
    private readonly GLShader _temporal;
    private readonly GLShader _resolve;
    private readonly uint _vao;

    private readonly RenderTarget _target;
    private readonly RenderTarget _blurTarget;
    private readonly RenderTarget[] _history = new RenderTarget[2];   // each: [0]=color+confidence, [1]=view-Z
    private readonly RenderTarget _resolveTarget;

    private int  _write;
    private int  _output;
    private bool _hasHistory;
    private Matrix4x4 _prevViewProj;

    // last-seen sampling parameters, so a mid-session change invalidates history instead of
    // blending fresh, differently-sampled frames against stale ones from the old settings
    private float _lastMaxDistance;
    private int   _lastMaxSteps;
    private int   _lastRefineSteps;
    private float _lastThickness;
    private float _lastRoughnessCutoff;
    private float _lastSilhouetteThreshold;

    public uint ReflectionTexture => _resolveTarget.ColorTextures[0];

    public SSRPass(GL gl, SSRConfig config, uint width, uint height)
    {
        _gl = gl;
        _config = config;

        _resDivisor = config.HalfResolution ? 2u : 1u;

        _shader   = CreateShader("ssr.frag");
        _blur     = CreateShader("ssr_blur.frag");
        _temporal = CreateShader("ssr_temporal.frag");
        _resolve  = CreateShader("ssr_resolve.frag");

        _target        = CreateFilteredTarget(width, height, 1);
        _blurTarget    = CreateFilteredTarget(width, height, 1);
        _resolveTarget = CreateFilteredTarget(width, height, 1);

        _history[0] = CreateFilteredTarget(width, height, 2);
        _history[1] = CreateFilteredTarget(width, height, 2);

        _vao = gl.GenVertexArray();

        BindConstantTextureSlots();
    }

    private void BindConstantTextureSlots()
    {
        _shader.Use();
        _shader.SetUniform("uScene",    0);
        _shader.SetUniform("uDepth",    1);
        _shader.SetUniform("uNormal",   2);
        _shader.SetUniform("uMaterial", 3);

        _blur.Use();
        _blur.SetUniform("uSsr",      0);
        _blur.SetUniform("uMaterial", 1);

        _temporal.Use();
        _temporal.SetUniform("uCurrent",   0);
        _temporal.SetUniform("uHistory",   1);
        _temporal.SetUniform("uHistoryZ",  2);
        _temporal.SetUniform("uDepth",     3);

        _resolve.Use();
        _resolve.SetUniform("uSsr",          0);
        _resolve.SetUniform("uDepth",        1);
        _resolve.SetUniform("uNormal",       2);
        _resolve.SetUniform("uMaterial",     3);
        _resolve.SetUniform("uPrefilterMap", 4);
        _resolve.SetUniform("uBrdfLUT",      5);
        _resolve.SetUniform("uProbeMap",     6);
        _resolve.SetUniform("uGtaoMap",      7);
        _resolve.SetUniform("uPlanarMap",    8);
    }
    public void Resize(uint width, uint height)
    {
        _target.Resize(width / _resDivisor, height / _resDivisor);
        _blurTarget.Resize(width / _resDivisor, height / _resDivisor);
        _history[0].Resize(width / _resDivisor, height / _resDivisor);
        _history[1].Resize(width / _resDivisor, height / _resDivisor);
        _resolveTarget.Resize(width / _resDivisor, height / _resDivisor);
        _hasHistory = false;   // previous frames are invalid at the new resolution
    }

    public void Render(uint sceneTex, in GBufferTextures gBuffer, Camera camera,
        in IblResolveInputs ibl, uint gtaoTex, bool gtaoActive, in PlanarResolveInputs planar)
    {
        var proj = camera.GetProjectionMatrix();
        Matrix4x4.Invert(proj, out var invProj);
        Matrix4x4.Invert(camera.GetViewMatrix(), out var invView);

        var viewProj = camera.GetViewMatrix() * proj;
        Matrix4x4.Invert(viewProj, out var invViewProj);

        ValidateHistory(viewProj);

        SetRenderState();

        RenderMarch(sceneTex, gBuffer, proj, invProj, invView, planar);
        RenderBlur(gBuffer.Material);
        RenderTemporal(gBuffer.Depth, invProj, invViewProj);
        RenderResolve(gBuffer, invProj, invView, ibl, gtaoTex, gtaoActive, planar);
        
        ResetRenderState();

        SwapHistoryBuffers(viewProj);
    }
    
    private void ValidateHistory(Matrix4x4 viewProj)
    {
        var settingsChanged =
            Math.Abs(_config.MaxDistance - _lastMaxDistance) > Tolerance ||
            _config.MaxSteps != _lastMaxSteps ||
            _config.RefineSteps != _lastRefineSteps ||
            Math.Abs(_config.Thickness - _lastThickness) > Tolerance ||
            Math.Abs(_config.RoughnessCutoff - _lastRoughnessCutoff) > Tolerance ||
            Math.Abs(_config.SilhouetteThreshold - _lastSilhouetteThreshold) > Tolerance;

        if (settingsChanged)
        {
            _hasHistory = false;

            _lastMaxDistance         = _config.MaxDistance;
            _lastMaxSteps            = _config.MaxSteps;
            _lastRefineSteps         = _config.RefineSteps;
            _lastThickness           = _config.Thickness;
            _lastRoughnessCutoff     = _config.RoughnessCutoff;
            _lastSilhouetteThreshold = _config.SilhouetteThreshold;
        }

        if (!_hasHistory)
            _prevViewProj = viewProj;
    }
    
    private void SwapHistoryBuffers(Matrix4x4 currentViewProj)
    {
        _output = _write;
        _write ^= 1;

        _prevViewProj = currentViewProj;
        _hasHistory = true;
    }
    
    private RenderTarget CreateFilteredTarget(uint width, uint height, int colorAttachments)
    {
        var formats = Enumerable
            .Repeat(InternalFormat.Rgba16f, colorAttachments)
            .ToArray();

        return new RenderTarget(_gl, width / _resDivisor, height / _resDivisor, 
            formats, withDepth: false, filter: GLEnum.Linear);
    }
    
    private GLShader CreateShader(string fragmentShader)
    {
        return new GLShader(
            _gl,
            PathResolver.Resolve("Shaders/Post/post.vert"),
            PathResolver.Resolve($"Shaders/SSR/{fragmentShader}"));
    }

    // Ray-marches the depth buffer and samples the resolved scene to find each pixel's reflection hit.
    private void RenderMarch(uint sceneTex, in GBufferTextures gBuffer, Matrix4x4 proj, Matrix4x4 invProj,
        Matrix4x4 invView, in PlanarResolveInputs planar)
    {
        _target.Bind();
        _target.Clear(0f, 0f, 0f, 0f);

        _shader.Use();
        _shader.SetUniform("uProjection",     proj);
        _shader.SetUniform("uInvProjection",  invProj);
        _shader.SetUniform("uMaxDistance",    _config.MaxDistance);
        _shader.SetUniform("uMaxSteps",       Math.Max(1, _config.MaxSteps));
        _shader.SetUniform("uRefineSteps",    Math.Max(0, _config.RefineSteps));
        _shader.SetUniform("uThickness",      _config.Thickness);
        _shader.SetUniform("uIntensity",      _config.Intensity);
        _shader.SetUniform("uRoughnessCutoff", _config.RoughnessCutoff);
        _shader.SetUniform("uSilhouetteThreshold", _config.SilhouetteThreshold);
        _shader.SetUniform("uTexel", new Vector2(1f / _target.Width, 1f / _target.Height));

        _shader.SetUniform("uInvView",      invView);
        _shader.SetUniform("uHasPlanar",    planar.Has ? 1 : 0);
        _shader.SetUniform("uPlanarHeight", planar.Height);

        Bind(TextureUnit.Texture0, sceneTex);
        Bind(TextureUnit.Texture1, gBuffer.Depth);
        Bind(TextureUnit.Texture2, gBuffer.Normal);
        Bind(TextureUnit.Texture3, gBuffer.Material);
        DrawFullscreen();
    }

    // Blurs the raw hit buffer, spread proportional to surface roughness.
    private void RenderBlur(uint materialTex)
    {
        _blurTarget.Bind();
        _blur.Use();
        _blur.SetUniform("uTexel", new Vector2(1f / _blurTarget.Width, 1f / _blurTarget.Height));
        _blur.SetUniform("uRoughnessCutoff", _config.RoughnessCutoff);

        Bind(TextureUnit.Texture0, _target.ColorTextures[0]);
        Bind(TextureUnit.Texture1, materialTex);

        DrawFullscreen();
    }

    // Reprojects and blends the previous frame's resolved (color, confidence) using a stored
    // view-space Z to reject history that doesn't belong to the same surface (silhouette bleed,
    // disocclusion) — see ssr_temporal.frag.
    private void RenderTemporal(uint depthTex, Matrix4x4 invProj, Matrix4x4 invViewProj)
    {
        var write = _history[_write];
        var read  = _history[_write ^ 1];

        write.Bind();
        _temporal.Use();
        _temporal.SetUniform("uInvProjection", invProj);
        _temporal.SetUniform("uInvViewProj",   invViewProj);
        _temporal.SetUniform("uPrevViewProj",  _prevViewProj);
        _temporal.SetUniform("uFeedback", _hasHistory ? _config.TemporalFeedback : 0f);

        Bind(TextureUnit.Texture0, _blurTarget.ColorTextures[0]);
        Bind(TextureUnit.Texture1, read.ColorTextures[0]);
        Bind(TextureUnit.Texture2, read.ColorTextures[1]);
        Bind(TextureUnit.Texture3, depthTex);
        DrawFullscreen();
    }

    // Blends the temporally-accumulated SSR hits against the IBL/probe/planar fallbacks into the final reflection term.
    private void RenderResolve(in GBufferTextures gBuffer, Matrix4x4 invProj, Matrix4x4 invView,
        in IblResolveInputs ibl, uint gtaoTex, bool gtaoActive, in PlanarResolveInputs planar)
    {
        _resolveTarget.Bind();
        _resolveTarget.Clear(0f, 0f, 0f, 0f);

        _resolve.Use();

        _resolve.SetUniform("uInvProjection",    invProj);
        _resolve.SetUniform("uInvView",          invView);
        _resolve.SetUniform("uMaxReflectionLod", ibl.MaxReflectionLod);
        _resolve.SetUniform("uIblIntensity",     ibl.Intensity);
        _resolve.SetUniform("uHasIBL",           ibl.HasIbl ? 1 : 0);

        _resolve.SetUniform("uProbeMaxReflectionLod", ibl.ProbeMaxReflectionLod);
        _resolve.SetUniform("uProbeIntensity",        ibl.ProbeIntensity);
        _resolve.SetUniform("uHasProbe",              ibl.HasProbe ? 1 : 0);
        _resolve.SetUniform("uProbePosition",         ibl.ProbePosition);
        _resolve.SetUniform("uProbeBoxMin",           ibl.ProbeBoxMin);
        _resolve.SetUniform("uProbeBoxMax",           ibl.ProbeBoxMax);
        _resolve.SetUniform("uProbeBoxFalloff",       ibl.ProbeBoxFalloff);

        _resolve.SetUniform("uHasGtao",  gtaoActive ? 1 : 0);

        _resolve.SetUniform("uHasPlanar",        planar.Has ? 1 : 0);
        _resolve.SetUniform("uPlanarHeight",     planar.Height);
        _resolve.SetUniform("uPlanarIntensity",  planar.Intensity);
        _resolve.SetUniform("uPlanarDistortion", planar.Distortion);
        _resolve.SetUniform("uPlanarBlur",       planar.Blur);

        Bind(TextureUnit.Texture0, _history[_write].ColorTextures[0]);
        Bind(TextureUnit.Texture1, gBuffer.Depth);
        Bind(TextureUnit.Texture2, gBuffer.Normal);
        Bind(TextureUnit.Texture3, gBuffer.Material);
        BindCube(TextureUnit.Texture4, ibl.PrefilterMap);
        Bind(TextureUnit.Texture5, ibl.BrdfLut);
        BindCube(TextureUnit.Texture6, ibl.ProbePrefilterMap);
        Bind(TextureUnit.Texture7, gtaoTex);
        Bind(TextureUnit.Texture8, planar.Map);

        DrawFullscreen();
    }

    private void Bind(TextureUnit unit, uint tex)
    {
        _gl.ActiveTexture(unit);
        _gl.BindTexture(TextureTarget.Texture2D, tex);
    }

    private void BindCube(TextureUnit unit, uint tex)
    {
        _gl.ActiveTexture(unit);
        _gl.BindTexture(TextureTarget.TextureCubeMap, tex);
    }

    private void DrawFullscreen()
    {
        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        _gl.BindVertexArray(0);
    }

    private void SetRenderState()
    {
        _gl.Disable(EnableCap.DepthTest);
    }

    private void ResetRenderState()
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.Enable(EnableCap.DepthTest);
    }

    public void Dispose()
    {
        _shader.Dispose();
        _blur.Dispose();
        _temporal.Dispose();
        _resolve.Dispose();
        _target.Dispose();
        _blurTarget.Dispose();
        _resolveTarget.Dispose();
        _history[0].Dispose();
        _history[1].Dispose();
        
        _gl.DeleteVertexArray(_vao);
    }
}
