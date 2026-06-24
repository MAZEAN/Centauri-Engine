namespace Centauri.Rendering.Postprocessing;

using Silk.NET.OpenGL;
using System.Numerics;

using Config;
using Graphics.Resources;
using Utils.Misc;
using Targets;

// Physically-flavored bloom via a mip pyramid (Jimenez / CoD "Next Generation Post
// Processing"): threshold + downsample the resolved HDR scene into progressively smaller
// mips, then upsample back up additively. The accumulated mip0 is the bloom the tonemap
// pass adds to the scene. All mips are half-or-smaller resolution, so this is cheap.
public sealed class BloomPass : IDisposable
{
    private const int MipCount = 6;

    private readonly GL _gl;
    private readonly BloomConfig _config;
    
    private readonly GLShader _prefilter;
    private readonly GLShader _down;
    private readonly GLShader _up;
    
    private readonly uint _vao;

    private readonly RenderTarget[] _mips = new RenderTarget[MipCount];

    public uint BloomTexture => _mips[0].ColorTextures[0];

    public BloomPass(GL gl, BloomConfig config, uint width, uint height)
    {
        _gl = gl;
        _config = config;

        _prefilter = Load(gl, "Assets/Shaders/Post/bloom_prefilter.frag");
        _down      = Load(gl, "Assets/Shaders/Post/bloom_down.frag");
        _up        = Load(gl, "Assets/Shaders/Post/bloom_up.frag");

        for (var i = 0; i < MipCount; i++)
        {
            var (w, h) = MipSize(width, height, i);
            _mips[i] = new RenderTarget(gl, w, h, [InternalFormat.Rgba16f], withDepth: false,
                filter: GLEnum.Linear);
        }

        _vao = gl.GenVertexArray();
    }

    public void Resize(uint width, uint height)
    {
        for (var i = 0; i < MipCount; i++)
        {
            var (w, h) = MipSize(width, height, i);
            _mips[i].Resize(w, h);
        }
    }

    // Reads the resolved HDR scene, leaves the accumulated bloom in mip0. Restores no GL
    // state beyond what it sets (caller rebinds the default framebuffer for the tonemap).
    public void Render(uint hdrResolved)
    {
        SetRenderState();

        // ── prefilter: resolved HDR → mip0 (threshold + box downsample) ──
        _mips[0].Bind();
        _prefilter.Use();
        _prefilter.SetUniform("uSrc", 0);
        _prefilter.SetUniform("uThreshold", _config.Threshold);
        _prefilter.SetUniform("uKnee", Math.Max(_config.Knee, 1e-4f));
        
        SetTexel(_prefilter, FullSizeFrom(0));   // source is the full-res resolved scene
        Bind(TextureUnit.Texture0, hdrResolved);
        DrawFullscreen();

        // ── downsample chain: mip[i-1] → mip[i] ──
        _down.Use();
        _down.SetUniform("uSrc", 0);
        for (var i = 1; i < MipCount; i++)
        {
            _mips[i].Bind();
            SetTexel(_down, new Vector2(_mips[i - 1].Width, _mips[i - 1].Height));
            Bind(TextureUnit.Texture0, _mips[i - 1].ColorTextures[0]);
            DrawFullscreen();
        }

        // ── upsample chain: mip[i+1] → add onto mip[i] ──
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.One, BlendingFactor.One);   // additive
        
        _up.Use();
        _up.SetUniform("uSrc", 0);
        _up.SetUniform("uRadius", _config.Radius);
        
        for (var i = MipCount - 2; i >= 0; i--)
        {
            _mips[i].Bind();
            SetTexel(_up, new Vector2(_mips[i + 1].Width, _mips[i + 1].Height));
            Bind(TextureUnit.Texture0, _mips[i + 1].ColorTextures[0]);
            DrawFullscreen();
        }
        
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
        new(gl, PathResolver.Resolve("Assets/Shaders/Post/post.vert"), PathResolver.Resolve(frag));

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
    
    private void ResetRenderState()
    {
        _gl.Disable(EnableCap.Blend);
        _gl.Enable(EnableCap.DepthTest);
    }

    public void Dispose()
    {
        _prefilter.Dispose();
        _down.Dispose();
        _up.Dispose();
        
        foreach (var mip in _mips)
            mip.Dispose();
        
        _gl.DeleteVertexArray(_vao);
    }
}
