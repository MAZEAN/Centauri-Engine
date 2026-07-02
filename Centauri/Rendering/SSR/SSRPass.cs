namespace Centauri.Rendering.SSR;

using Silk.NET.OpenGL;
using System.Numerics;

using Config;
using World;
using Graphics.Resources;
using Utils.Misc;
using Targets;

// Screen-space reflections: ray-marches the prepass depth buffer and samples the resolved
// HDR scene to build a reflection term, weighted per-surface by the prepass material buffer
// (roughness/metallic). The reflection is then RESOLVED against the IBL specular fallback so
// that, where SSR is confident, it replaces the environment reflection the lit pass already
// applied (rather than stacking on it); where it is not, the scene keeps that fallback. The
// result is the delta the post stack folds into the scene before tonemapping.
// See Shaders/SSR/ssr.frag (march), ssr_blur.frag (roughness blur) and
// ssr_resolve.frag (IBL blend).
public sealed class SSRPass : IDisposable
{
    private readonly GL _gl;
    private readonly SSRConfig _config;
    
    private readonly uint _resDivisor;
    
    private readonly GLShader _shader;
    private readonly GLShader _blur;
    private readonly GLShader _resolve;
    private readonly uint _vao;

    private readonly RenderTarget _target;
    private readonly RenderTarget _blurTarget;
    private readonly RenderTarget _resolveTarget;

    public uint ReflectionTexture => _resolveTarget.ColorTextures[0];
    
    public SSRPass(GL gl, SSRConfig config, uint width, uint height)
    {
        _gl = gl;
        _config = config;
        
        _resDivisor = config.HalfResolution ? 2u : 1u;
        
        _shader = new GLShader(gl,
            PathResolver.Resolve("Shaders/Post/post.vert"),
            PathResolver.Resolve("Shaders/SSR/ssr.frag"));
        _blur = new GLShader(gl,
            PathResolver.Resolve("Shaders/Post/post.vert"),
            PathResolver.Resolve("Shaders/SSR/ssr_blur.frag"));
        _resolve = new GLShader(gl,
            PathResolver.Resolve("Shaders/Post/post.vert"),
            PathResolver.Resolve("Shaders/SSR/ssr_resolve.frag"));
        
        _target = new RenderTarget(gl, width / _resDivisor, height / _resDivisor,
            [InternalFormat.Rgba16f], withDepth: false, filter: GLEnum.Linear);
        _blurTarget = new RenderTarget(gl, width / _resDivisor, height / _resDivisor,
            [InternalFormat.Rgba16f], withDepth: false, filter: GLEnum.Linear);
        _resolveTarget = new RenderTarget(gl, width / _resDivisor, height / _resDivisor,
            [InternalFormat.Rgba16f], withDepth: false, filter: GLEnum.Linear);
        
        _vao = gl.GenVertexArray();
    }

    public void Resize(uint width, uint height)
    {
        _target.Resize(width / _resDivisor, height / _resDivisor);
        _blurTarget.Resize(width / _resDivisor, height / _resDivisor);
        _resolveTarget.Resize(width / _resDivisor, height / _resDivisor);
    }

    public void Render(uint sceneTex, uint depthTex, uint normalTex, uint materialTex, Camera camera,
        uint prefilterMap, uint brdfLut, float maxReflectionLod, float iblIntensity, bool hasIbl,
        uint probeMap, float probeMaxReflectionLod, float probeIntensity, bool hasProbe,
        Vector3 probePosition, Vector3 probeBoxMin, Vector3 probeBoxMax, float probeBoxFalloff,
        uint ssaoTex, bool ssaoActive)
    {
        var proj = camera.GetProjectionMatrix();
        Matrix4x4.Invert(proj, out var invProj);
        Matrix4x4.Invert(camera.GetViewMatrix(), out var invView);

        SetRenderState();
        
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

        _shader.SetUniform("uScene",    0);
        _shader.SetUniform("uDepth",    1);
        _shader.SetUniform("uNormal",   2);
        _shader.SetUniform("uMaterial", 3);
        
        Bind(TextureUnit.Texture0, sceneTex);
        Bind(TextureUnit.Texture1, depthTex);
        Bind(TextureUnit.Texture2, normalTex);
        Bind(TextureUnit.Texture3, materialTex);
        DrawFullscreen();

        _blurTarget.Bind();
        _blur.Use();
        _blur.SetUniform("uSsr",      0);
        _blur.SetUniform("uMaterial", 1);
        _blur.SetUniform("uTexel", new Vector2(1f / _blurTarget.Width, 1f / _blurTarget.Height));
        _blur.SetUniform("uRoughnessCutoff", _config.RoughnessCutoff);
        
        Bind(TextureUnit.Texture0, _target.ColorTextures[0]);
        Bind(TextureUnit.Texture1, materialTex);
        
        DrawFullscreen();
        
        _resolveTarget.Bind();
        _resolveTarget.Clear(0f, 0f, 0f, 0f);
        
        _resolve.Use();
        _resolve.SetUniform("uSsr",          0);
        _resolve.SetUniform("uDepth",        1);
        _resolve.SetUniform("uNormal",       2);
        _resolve.SetUniform("uMaterial",     3);
        _resolve.SetUniform("uPrefilterMap", 4);
        _resolve.SetUniform("uBrdfLUT",      5);
        _resolve.SetUniform("uProbeMap",     6);
        
        _resolve.SetUniform("uInvProjection",    invProj);
        _resolve.SetUniform("uInvView",          invView);
        _resolve.SetUniform("uMaxReflectionLod", maxReflectionLod);
        _resolve.SetUniform("uIblIntensity",     iblIntensity);
        _resolve.SetUniform("uHasIBL",           hasIbl ? 1 : 0);
        
        _resolve.SetUniform("uProbeMaxReflectionLod", probeMaxReflectionLod);
        _resolve.SetUniform("uProbeIntensity",        probeIntensity);
        _resolve.SetUniform("uHasProbe",              hasProbe ? 1 : 0);
        _resolve.SetUniform("uProbePosition",         probePosition);
        _resolve.SetUniform("uProbeBoxMin",           probeBoxMin);
        _resolve.SetUniform("uProbeBoxMax",           probeBoxMax);
        _resolve.SetUniform("uProbeBoxFalloff",       probeBoxFalloff);

        _resolve.SetUniform("uSsaoMap",  7);
        _resolve.SetUniform("uHasSSAO",  ssaoActive ? 1 : 0);

        Bind(TextureUnit.Texture0, _blurTarget.ColorTextures[0]);
        Bind(TextureUnit.Texture1, depthTex);
        Bind(TextureUnit.Texture2, normalTex);
        Bind(TextureUnit.Texture3, materialTex);
        BindCube(TextureUnit.Texture4, prefilterMap);
        Bind(TextureUnit.Texture5, brdfLut);
        BindCube(TextureUnit.Texture6, probeMap);
        Bind(TextureUnit.Texture7, ssaoTex);

        DrawFullscreen();

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        
        ResetRenderState();
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
        _gl.Enable(EnableCap.DepthTest);
    }

    public void Dispose()
    {
        _shader.Dispose();
        _blur.Dispose();
        _resolve.Dispose();
        _target.Dispose();
        _blurTarget.Dispose();
        _resolveTarget.Dispose();
        _gl.DeleteVertexArray(_vao);
    }
}
