namespace Centauri.Rendering.Postprocessing;

using Silk.NET.OpenGL;
using System.Numerics;

using Config;
using Graphics.Resources;
using Utils.Misc;
using Targets;

// Eye adaptation. Downsamples the resolved HDR scene into log-luminance at half resolution
// (one manual pass, see luminance_prefilter.frag), then hands the rest of the reduction down to
// 1x1 to the GPU's native mipmap generator instead of chaining ~10 more CPU-issued FBO-bind +
// draw-call passes — glGenerateMipmap does the same per-level box-filter reduction the old manual
// chain did (a plain 4-tap average per step) in one driver call, at a fraction of the CPU overhead
// each small manual pass carried (bind, program switch, uniform sets, draw — repeated ~10x for
// targets too tiny to cost meaningful GPU time in the first place). That reading is then blended
// into a persistent, ping-ponged 1x1 "adapted" texture over real time. Everything stays
// GPU-resident: there is no CPU readback of the measured luminance, which would otherwise stall
// the pipeline waiting on the GPU once every frame. The tonemap pass
// instead samples the adapted texture directly and computes its own exposure multiplier.
//
// The luminance value only ever needs the R channel, but RenderTarget always allocates
// RGBA (see GTAOPass for the same convention) — G/B/A just go unused here.
public sealed class AutoExposurePass : IDisposable
{
    private readonly GL _gl;
    private readonly AutoExposureConfig _config;

    private readonly GLShader _prefilter;
    private readonly GLShader _adapt;

    private readonly uint _vao;

    private readonly RenderTarget _prefilterTarget;         // half-res log-luminance; mipmapped down to ~1x1
    private readonly RenderTarget[] _adapted = new RenderTarget[2];   // ping-pong, 1x1

    // Mip level of _prefilterTarget that reduces it to 1x1 (or the smallest a non-power-of-two
    // size reaches) — textureLod in luminance_adapt.frag reads directly from this level instead
    // of relying on implicit LOD selection, which a fullscreen triangle's near-zero screen-space
    // derivatives can't be trusted to pick correctly.
    private float _prefilterMaxLevel;

    private int  _current;      // index into _adapted holding the latest adapted value
    private bool _firstFrame = true;

    public uint AdaptedLuminanceTexture => _adapted[_current].ColorTextures[0];

    public AutoExposurePass(GL gl, AutoExposureConfig config, uint width, uint height)
    {
        _gl = gl;
        _config = config;

        _prefilter = Load(gl, "Shaders/Post/luminance_prefilter.frag");
        _adapt     = Load(gl, "Shaders/Post/luminance_adapt.frag");

        _prefilterTarget = new RenderTarget(gl, Math.Max(1u, width / 2), Math.Max(1u, height / 2),
            [InternalFormat.Rgba16f], withDepth: false, filter: GLEnum.Linear);
        EnableMipmapFiltering();
        _prefilterMaxLevel = ComputeMaxLevel(_prefilterTarget.Width, _prefilterTarget.Height);

        _adapted[0] = new RenderTarget(gl, 1, 1, [InternalFormat.Rgba16f], withDepth: false, filter: GLEnum.Nearest);
        _adapted[1] = new RenderTarget(gl, 1, 1, [InternalFormat.Rgba16f], withDepth: false, filter: GLEnum.Nearest);

        _vao = gl.GenVertexArray();
    }

    public void Resize(uint width, uint height)
    {
        _prefilterTarget.Resize(Math.Max(1u, width / 2), Math.Max(1u, height / 2));
        EnableMipmapFiltering();
        _prefilterMaxLevel = ComputeMaxLevel(_prefilterTarget.Width, _prefilterTarget.Height);
        // _adapted stay 1x1 regardless of resize — nothing to do there.
    }

    // RenderTarget's own sampler setup has no mipmap-filtering variant (every other consumer
    // samples a single level) — Resize() reallocates the texture, so this has to be reapplied
    // every time, not just once at construction.
    private void EnableMipmapFiltering()
    {
        _gl.BindTexture(TextureTarget.Texture2D, _prefilterTarget.ColorTextures[0]);
        GLSampler.Set(_gl, TextureTarget.Texture2D,
            GLEnum.ClampToEdge, GLEnum.LinearMipmapLinear, GLEnum.Linear);
    }

    private static float ComputeMaxLevel(uint width, uint height) =>
        MathF.Floor(MathF.Log2(Math.Max(width, height)));

    // Reads the resolved HDR scene, leaves this frame's adapted luminance in AdaptedLuminanceTexture.
    public void Render(uint hdrResolved, float deltaTime)
    {
        SetRenderState();

        // ── prefilter: resolved HDR → log-luminance, half-res ──
        _prefilterTarget.Bind();
        _prefilter.Use();
        _prefilter.SetUniform("uSrc", 0);
        SetTexel(_prefilter, new Vector2(_prefilterTarget.Width * 2f, _prefilterTarget.Height * 2f));
        Bind(TextureUnit.Texture0, hdrResolved);
        DrawFullscreen();

        // ── hand the rest of the reduction (half-res -> ~1x1) to hardware mip generation ──
        _gl.BindTexture(TextureTarget.Texture2D, _prefilterTarget.ColorTextures[0]);
        _gl.GenerateMipmap(TextureTarget.Texture2D);

        // ── adapt: blend this frame's ~1x1 reading into the persistent adapted value ──
        var next = 1 - _current;
        _adapted[next].Bind();
        _adapt.Use();
        _adapt.SetUniform("uCurrent",    0);
        _adapt.SetUniform("uPrevious",   1);
        _adapt.SetUniform("uCurrentLod", _prefilterMaxLevel);
        // _adapted[_current] holds whatever the driver zero-initialized it to until the first
        // real frame — skip adapting from that by snapping straight to the measured value.
        _adapt.SetUniform("uAdaptSpeed", _firstFrame ? 1000f : _config.AdaptSpeed);
        _adapt.SetUniform("uDeltaTime",  deltaTime);

        Bind(TextureUnit.Texture0, _prefilterTarget.ColorTextures[0]);
        Bind(TextureUnit.Texture1, _adapted[_current].ColorTextures[0]);
        DrawFullscreen();

        _current    = next;
        _firstFrame = false;

        ResetRenderState();
    }

    private static void SetTexel(GLShader shader, Vector2 sourceSize) =>
        shader.SetUniform("uTexel", new Vector2(1f / sourceSize.X, 1f / sourceSize.Y));

    private static GLShader Load(GL gl, string frag) =>
        new(gl, PathResolver.Resolve("Shaders/Post/post.vert"), PathResolver.Resolve(frag));

    private void Bind(TextureUnit unit, uint tex)
    {
        _gl.ActiveTexture(unit);
        _gl.BindTexture(TextureTarget.Texture2D, tex);
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
        _gl.Disable(EnableCap.Blend);
    }

    private void ResetRenderState() => _gl.Enable(EnableCap.DepthTest);

    public void Dispose()
    {
        _prefilter.Dispose();
        _adapt.Dispose();

        _prefilterTarget.Dispose();
        foreach (var t in _adapted)
            t.Dispose();

        _gl.DeleteVertexArray(_vao);
    }
}
