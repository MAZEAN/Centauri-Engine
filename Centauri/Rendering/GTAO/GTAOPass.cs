namespace Centauri.Rendering.GTAO;

using Silk.NET.OpenGL;
using System.Numerics;

using Config;
using World;
using Graphics.Resources;
using Utils.Misc;
using Targets;

// GTAO: multi-slice horizon search against the prepass depth + view-space normals, a depth-aware
// 4x4 box blur to remove the spatial noise pattern, then a temporal accumulation pass that
// reprojects the previous frame's result against a per-frame rotating noise pattern (see
// gtao.frag/gtao_temporal.frag) so fewer spatial samples are needed for a stable result. Produces
// a single AO factor the lit pass multiplies into the ambient/IBL term.
public sealed class GTAOPass : IDisposable
{
    private const int  NoiseDim    = 4;
    private const uint ResDivisor = 2;   // half-res

    private readonly GL _gl;
    private readonly GTAOConfig _config;
    
    private readonly GLShader _gtao;
    private readonly GLShader _blur;
    private readonly GLShader _temporal;
    private readonly uint _vao;     // empty VAO for attribute-less fullscreen draws
    private readonly uint _noise;

    private readonly RenderTarget _aoTarget;
    private readonly RenderTarget _blurTarget;
    private readonly RenderTarget[] _history = new RenderTarget[2];

    private int  _write;     // history slot to render into this frame
    private int  _output;    // history slot holding the latest resolved result
    private bool _hasHistory;

    private int _frame;
    private Matrix4x4 _prevViewProj;

    public uint AoTexture => _history[_output].ColorTextures[0];

    public GTAOPass(GL gl, GTAOConfig config, uint width, uint height)
    {
        _gl = gl;
        _config = config;

        _gtao = new GLShader(gl,
            PathResolver.Resolve("Shaders/Post/post.vert"),
            PathResolver.Resolve("Shaders/GTAO/gtao.frag"));
        _blur = new GLShader(gl,
            PathResolver.Resolve("Shaders/Post/post.vert"),
            PathResolver.Resolve("Shaders/GTAO/gtao_blur.frag"));
        _temporal = new GLShader(gl,
            PathResolver.Resolve("Shaders/Post/post.vert"),
            PathResolver.Resolve("Shaders/GTAO/gtao_temporal.frag"));
        _aoTarget   = new RenderTarget(gl, width / ResDivisor, height / ResDivisor,
            [InternalFormat.Rgba16f], withDepth: false);
        _blurTarget = new RenderTarget(gl, width / ResDivisor, height / ResDivisor,
            [InternalFormat.Rgba16f], withDepth: false, filter: GLEnum.Linear);
        _history[0] = new RenderTarget(gl, width / ResDivisor, height / ResDivisor,
            [InternalFormat.Rgba16f], withDepth: false, filter: GLEnum.Linear);
        _history[1] = new RenderTarget(gl, width / ResDivisor, height / ResDivisor,
            [InternalFormat.Rgba16f], withDepth: false, filter: GLEnum.Linear);

        _vao   = gl.GenVertexArray();
        _noise = CreateNoise();
    }

    public void Resize(uint width, uint height)
    {
        _aoTarget.Resize(width / ResDivisor, height / ResDivisor);
        _blurTarget.Resize(width / ResDivisor, height / ResDivisor);
        _history[0].Resize(width / ResDivisor, height / ResDivisor);
        _history[1].Resize(width / ResDivisor, height / ResDivisor);
        _hasHistory = false;   // previous frames are invalid at the new resolution
    }

    public void Render(uint depthTex, uint normalTex, Camera camera)
    {
        var proj = camera.GetProjectionMatrix();
        Matrix4x4.Invert(proj, out var invProj);
        
        var viewProj = camera.GetViewMatrix() * proj;
        Matrix4x4.Invert(viewProj, out var invViewProj);

        if (!_hasHistory)
            _prevViewProj = viewProj;

        _frame++;

        _gl.Disable(EnableCap.DepthTest);

        // ── occlusion ──
        _aoTarget.Bind();
        _aoTarget.Clear(1f, 1f, 1f, 1f);

        _gtao.Use();
        _gtao.SetUniform("uProjection",    proj);
        _gtao.SetUniform("uInvProjection", invProj);
        _gtao.SetUniform("uRadius",     _config.Radius);
        _gtao.SetUniform("uSliceCount", Math.Max(1, _config.SliceCount));
        _gtao.SetUniform("uStepCount",  Math.Max(1, _config.StepCount));
        _gtao.SetUniform("uPower",      _config.Power);
        _gtao.SetUniform("uFrameIndex", _frame);

        _gtao.SetUniform("uDepth",  0);
        _gtao.SetUniform("uNormal", 1);
        _gtao.SetUniform("uNoise",  2);
        
        Bind(TextureUnit.Texture0, depthTex);
        Bind(TextureUnit.Texture1, normalTex);
        Bind(TextureUnit.Texture2, _noise);
        DrawFullscreen();
        
        // ── blur ──
        _blurTarget.Bind();
        
        _blur.Use();
        _blur.SetUniform("uGtao", 0);
        _blur.SetUniform("uDepth", 1);
        _blur.SetUniform("uInvProjection", invProj);
        
        Bind(TextureUnit.Texture0, _aoTarget.ColorTextures[0]);
        Bind(TextureUnit.Texture1, depthTex);

        // ── temporal accumulation ──
        var write = _history[_write];
        var read  = _history[_write ^ 1];

        write.Bind();
        _temporal.Use();
        _temporal.SetUniform("uCurrent", 0);
        _temporal.SetUniform("uHistory", 1);
        _temporal.SetUniform("uDepth",   2);
        _temporal.SetUniform("uInvViewProj",  invViewProj);
        _temporal.SetUniform("uPrevViewProj", _prevViewProj);
        _temporal.SetUniform("uTexel", new Vector2(1f / write.Width, 1f / write.Height));
        _temporal.SetUniform("uFeedback", _hasHistory ? _config.TemporalFeedback : 0f);

        Bind(TextureUnit.Texture0, _blurTarget.ColorTextures[0]);
        Bind(TextureUnit.Texture1, read.ColorTextures[0]);
        Bind(TextureUnit.Texture2, depthTex);
        DrawFullscreen();

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.Enable(EnableCap.DepthTest);

        _output       = _write;
        _write       ^= 1;
        _prevViewProj = viewProj;
        _hasHistory   = true;
    }

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
    
    // xy = per-pixel base rotation vector for the slice directions; z = a [0,1) jitter offset
    // for the horizon march's step positions — both exist purely to break up the banding a
    // fixed slice count/step spacing would otherwise show, diffused into a blur-able noise
    // pattern the same way the old kernel-sample SSAO's rotation noise did.
    private unsafe uint CreateNoise()
    {
        var rng  = new Random(2);
        var data = new float[NoiseDim * NoiseDim * 3];
        for (var i = 0; i < NoiseDim * NoiseDim; i++)
        {
            data[i * 3 + 0] = (float)(rng.NextDouble() * 2.0 - 1.0);
            data[i * 3 + 1] = (float)(rng.NextDouble() * 2.0 - 1.0);
            data[i * 3 + 2] = (float)rng.NextDouble();
        }

        var tex = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, tex);
        fixed (float* p = data)
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgb16f,
                NoiseDim, NoiseDim, 0, PixelFormat.Rgb, PixelType.Float, p);
        
        GLSampler.Set(_gl, TextureTarget.Texture2D, GLEnum.Repeat, GLEnum.Nearest, GLEnum.Nearest);
        return tex;
    }

    public void Dispose()
    {
        _gtao.Dispose();
        _blur.Dispose();
        _temporal.Dispose();
        _aoTarget.Dispose();
        _blurTarget.Dispose();
        _history[0].Dispose();
        _history[1].Dispose();
        
        _gl.DeleteTexture(_noise);
        _gl.DeleteVertexArray(_vao);
    }
}
