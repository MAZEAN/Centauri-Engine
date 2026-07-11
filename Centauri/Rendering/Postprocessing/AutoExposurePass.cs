namespace Centauri.Rendering.Postprocessing;

using Silk.NET.OpenGL;
using System.Numerics;

using Config;
using Graphics.Resources;
using Utils.Misc;
using Targets;

// Eye adaptation. Downsamples the resolved HDR scene into a log-luminance mip pyramid — the
// same box-filter mip-chain shape as BloomPass — ending at 1x1, then blends that "current"
// reading into a persistent, ping-ponged 1x1 "adapted" texture over real time. Everything stays
// GPU-resident: there is no CPU readback of the measured luminance, which would otherwise stall
// the pipeline waiting on the GPU once every frame. The tonemap pass instead samples the
// adapted texture directly and computes its own exposure multiplier.
//
// The luminance value only ever needs the R channel, but RenderTarget always allocates
// RGBA (see GTAOPass for the same convention) — G/B/A just go unused here.
public sealed class AutoExposurePass : IDisposable
{
    private const int MipCount = 12;   // halves 12x — reaches 1x1 well before this at any real resolution

    private readonly GL _gl;
    private readonly AutoExposureConfig _config;

    private readonly GLShader _prefilter;
    private readonly GLShader _down;
    private readonly GLShader _adapt;

    private readonly uint _vao;

    private readonly RenderTarget[] _mips = new RenderTarget[MipCount];
    private readonly RenderTarget[] _adapted = new RenderTarget[2];   // ping-pong, 1x1

    // Index of the first mip that's already 1x1 — the chain can't shrink further past this
    // point, so downsampling into the remaining allocated-but-unreached mips (see MipCount's
    // comment: 12 is sized to reach 1x1 "well before this at any real resolution", meaning some
    // tail is expected to go unused) would just be a 1x1 -> 1x1 no-op draw call. Render() stops
    // here instead of always issuing all MipCount-1 downsample draws.
    private int _lastMip;

    private int  _current;      // index into _adapted holding the latest adapted value
    private bool _firstFrame = true;

    public uint AdaptedLuminanceTexture => _adapted[_current].ColorTextures[0];

    public AutoExposurePass(GL gl, AutoExposureConfig config, uint width, uint height)
    {
        _gl = gl;
        _config = config;

        _prefilter = Load(gl, "Shaders/Post/luminance_prefilter.frag");
        _down      = Load(gl, "Shaders/Post/luminance_down.frag");
        _adapt     = Load(gl, "Shaders/Post/luminance_adapt.frag");

        for (var i = 0; i < MipCount; i++)
        {
            var (w, h) = MipSize(width, height, i);
            _mips[i] = new RenderTarget(gl, w, h, [InternalFormat.Rgba16f], withDepth: false, filter: GLEnum.Linear);
        }
        _lastMip = ComputeLastMip(width, height);

        _adapted[0] = new RenderTarget(gl, 1, 1, [InternalFormat.Rgba16f], withDepth: false, filter: GLEnum.Nearest);
        _adapted[1] = new RenderTarget(gl, 1, 1, [InternalFormat.Rgba16f], withDepth: false, filter: GLEnum.Nearest);

        _vao = gl.GenVertexArray();
    }

    public void Resize(uint width, uint height)
    {
        for (var i = 0; i < MipCount; i++)
        {
            var (w, h) = MipSize(width, height, i);
            _mips[i].Resize(w, h);
        }
        _lastMip = ComputeLastMip(width, height);
        // _adapted stay 1x1 regardless of resize — nothing to do there.
    }

    // First index whose size is already 1x1 (clamped to MipCount - 1 so a pathologically tiny
    // viewport that hits 1x1 before mip0 doesn't produce an out-of-range index).
    private static int ComputeLastMip(uint width, uint height)
    {
        for (var i = 0; i < MipCount; i++)
        {
            var (w, h) = MipSize(width, height, i);
            if (w == 1 && h == 1) 
                return i;
        }
        return MipCount - 1;
    }

    // Reads the resolved HDR scene, leaves this frame's adapted luminance in AdaptedLuminanceTexture.
    public void Render(uint hdrResolved, float deltaTime)
    {
        SetRenderState();

        // ── prefilter: resolved HDR → log-luminance, half-res ──
        _mips[0].Bind();
        _prefilter.Use();
        _prefilter.SetUniform("uSrc", 0);
        SetTexel(_prefilter, FullSizeFrom(0));
        Bind(TextureUnit.Texture0, hdrResolved);
        DrawFullscreen();

        // ── downsample chain: mip[i-1] → mip[i], ending at 1x1 (see _lastMip) ──
        _down.Use();
        _down.SetUniform("uSrc", 0);
        for (var i = 1; i <= _lastMip; i++)
        {
            _mips[i].Bind();
            SetTexel(_down, new Vector2(_mips[i - 1].Width, _mips[i - 1].Height));
            Bind(TextureUnit.Texture0, _mips[i - 1].ColorTextures[0]);
            DrawFullscreen();
        }

        // ── adapt: blend this frame's 1x1 reading into the persistent adapted value ──
        var next = 1 - _current;
        _adapted[next].Bind();
        _adapt.Use();
        _adapt.SetUniform("uCurrent",  0);
        _adapt.SetUniform("uPrevious", 1);
        // _adapted[_current] holds whatever the driver zero-initialized it to until the first
        // real frame — skip adapting from that by snapping straight to the measured value.
        _adapt.SetUniform("uAdaptSpeed", _firstFrame ? 1000f : _config.AdaptSpeed);
        _adapt.SetUniform("uDeltaTime",  deltaTime);

        Bind(TextureUnit.Texture0, _mips[_lastMip].ColorTextures[0]);
        Bind(TextureUnit.Texture1, _adapted[_current].ColorTextures[0]);
        DrawFullscreen();

        _current    = next;
        _firstFrame = false;

        ResetRenderState();
    }

    private static void SetTexel(GLShader shader, Vector2 sourceSize) =>
        shader.SetUniform("uTexel", new Vector2(1f / sourceSize.X, 1f / sourceSize.Y));

    // mip0 downsamples from the full-res resolved scene; its "source" is double its own size.
    private Vector2 FullSizeFrom(int mip) =>
        new(_mips[mip].Width * 2f, _mips[mip].Height * 2f);

    private static (uint w, uint h) MipSize(uint width, uint height, int mip)
    {
        var div = (uint)(1 << (mip + 1));   // mip0 = half-res, then halve each level
        return (Math.Max(1u, width / div), Math.Max(1u, height / div));
    }

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
        _down.Dispose();
        _adapt.Dispose();

        foreach (var mip in _mips)
            mip.Dispose();
        foreach (var t in _adapted)
            t.Dispose();

        _gl.DeleteVertexArray(_vao);
    }
}
