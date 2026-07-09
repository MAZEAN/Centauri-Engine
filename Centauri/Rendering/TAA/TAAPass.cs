namespace Centauri.Rendering.TAA;

using Silk.NET.OpenGL;
using System.Numerics;

using Config;
using World;
using Graphics.Resources;
using Utils.Misc;
using Targets;

// Temporal anti-aliasing. Each frame the scene is rendered with a Halton sub-pixel jitter
// (set on the camera by the renderer); this pass computes camera motion vectors from depth,
// reprojects the previous frame's result, variance-clips it against the current neighbourhood
// and blends — accumulating the jittered samples into a supersampled, stable image. Fixes
// edge aliasing and sub-pixel specular flicker. Camera-motion only (moving objects ghost).
public sealed class TAAPass : IDisposable
{
    private const int JitterPeriod = 8;

    private readonly GL _gl;
    private readonly TAAConfig _config;
    
    private readonly GLShader _velocity;
    private readonly GLShader _resolve;
    private readonly uint _vao;

    private readonly RenderTarget _velocityTarget;
    private readonly RenderTarget[] _history = new RenderTarget[2];
        
    private int  _write;     // slot to render into this frame
    private int  _output;    // slot holding the latest resolved result
    private bool _hasHistory;

    private int _frame;
    private Matrix4x4 _prevViewProj;

    public uint OutputTexture => _history[_output].ColorTextures[0];
    public uint VelocityTexture => _velocityTarget.ColorTextures[0];   // for the debug view

    public TAAPass(GL gl, TAAConfig config, uint width, uint height)
    {
        _gl = gl;
        _config = config;

        _velocity = new GLShader(gl,
            PathResolver.Resolve("Shaders/Post/post.vert"),
            PathResolver.Resolve("Shaders/TAA/velocity.frag"));
        _resolve = new GLShader(gl,
            PathResolver.Resolve("Shaders/Post/post.vert"),
            PathResolver.Resolve("Shaders/TAA/taa.frag"));

        _velocityTarget = new RenderTarget(gl, width, height, [InternalFormat.Rgba16f], withDepth: false);
        _history[0] = new RenderTarget(gl, width, height, [InternalFormat.Rgba16f], withDepth: false, filter: GLEnum.Linear);
        _history[1] = new RenderTarget(gl, width, height, [InternalFormat.Rgba16f], withDepth: false, filter: GLEnum.Linear);

        _vao = gl.GenVertexArray();
    }

    public void Resize(uint width, uint height)
    {
        _velocityTarget.Resize(width, height);
        _history[0].Resize(width, height);
        _history[1].Resize(width, height);
        _hasHistory = false;   // previous frames are invalid at the new resolution
    }
    
    public Vector2 NextJitter(uint width, uint height)
    {
        if (!_config.Enabled) return Vector2.Zero;

        _frame = (_frame + 1) % JitterPeriod;
        var jx = Halton(_frame + 1, 2) - 0.5f;   // [-0.5, 0.5] pixels
        var jy = Halton(_frame + 1, 3) - 0.5f;
        
        return new Vector2(jx * 2f / width, jy * 2f / height);
    }

    // Resolve TAA into the current history target; OutputTexture then holds the result and
    // serves as next frame's history.
    public void Render(uint sceneColor, uint ssrTex, bool hasSsr, uint depthTex, Camera camera)
    {
        var viewProj = camera.GetViewMatrix() * camera.GetProjectionMatrix();
        Matrix4x4.Invert(viewProj, out var invViewProj);

        if (!_hasHistory)
            _prevViewProj = viewProj;


        _gl.Disable(EnableCap.DepthTest);

        // ── motion vectors ──
        _velocityTarget.Bind();
        _velocityTarget.Clear(0f, 0f, 0f, 0f);
        _velocity.Use();
        _velocity.SetUniform("uDepth", 0);
        _velocity.SetUniform("uInvViewProj",  invViewProj);
        _velocity.SetUniform("uPrevViewProj", _prevViewProj);
        Bind(TextureUnit.Texture0, depthTex);
        DrawFullscreen();

        // ── resolve into the current history slot ──
        var write = _history[_write];
        var read  = _history[_write ^ 1];

        write.Bind();
        _resolve.Use();
        _resolve.SetUniform("uCurrent",  0);
        _resolve.SetUniform("uHistory",  1);
        _resolve.SetUniform("uVelocity", 2);
        _resolve.SetUniform("uSsr",      3);
        _resolve.SetUniform("uHasSsr",   hasSsr ? 1 : 0);
        _resolve.SetUniform("uTexel", new Vector2(1f / write.Width, 1f / write.Height));
        _resolve.SetUniform("uFeedback", _hasHistory ? _config.Feedback : 0f);
        
        Bind(TextureUnit.Texture0, sceneColor);
        Bind(TextureUnit.Texture1, read.ColorTextures[0]);
        Bind(TextureUnit.Texture2, _velocityTarget.ColorTextures[0]);
        Bind(TextureUnit.Texture3, hasSsr ? ssrTex : 0);
        DrawFullscreen();

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.Enable(EnableCap.DepthTest);

        _output       = _write;
        _write       ^= 1;
        _prevViewProj = viewProj;
        _hasHistory   = true;
    }

    private static float Halton(int index, int b)
    {
        float f = 1f, r = 0f;
        for (var i = index; i > 0; i /= b)
        {
            f /= b;
            r += f * (i % b);
        }
        
        return r;
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

    public void Dispose()
    {
        _velocity.Dispose();
        _resolve.Dispose();
        _velocityTarget.Dispose();
        _history[0].Dispose();
        _history[1].Dispose();
        _gl.DeleteVertexArray(_vao);
    }
}
