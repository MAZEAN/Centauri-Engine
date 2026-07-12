namespace Centauri.Rendering.GTAO;

using Silk.NET.OpenGL;
using System.Numerics;

using Config;
using World;
using Graphics.Resources;
using Utils.Misc;
using Targets;

public sealed class GTAOPass : IDisposable
{
    private const float Tolerance = 0.01f;
    private const int NoiseDim = 4;
    private const uint ResDivisor = 2;

    private readonly GL _gl;
    private readonly GTAOConfig _config;

    // Shaders
    private readonly GLShader _gtao;
    private readonly GLShader _blur;
    private readonly GLShader _temporal;

    // Resources
    private readonly uint _vao;
    private readonly uint _noise;

    // Render targets
    private readonly RenderTarget _aoTarget;
    private readonly RenderTarget _blurTarget;
    private readonly RenderTarget[] _history = new RenderTarget[2];

    // Temporal state
    private int _write;
    private int _output;
    private bool _hasHistory;
    private int _frame;

    private Matrix4x4 _prevViewProj;

    // Cached settings used to invalidate history when changed
    private float _lastRadius;
    private int _lastSliceCount;
    private int _lastStepCount;

    public uint AoTexture => _history[_output].ColorTextures[0];

    public GTAOPass(GL gl, GTAOConfig config, uint width, uint height)
    {
        _gl = gl;
        _config = config;

        _gtao = CreateShader("gtao.frag");
        _blur = CreateShader("gtao_blur.frag");
        _temporal = CreateShader("gtao_temporal.frag");

        var targetWidth = width / ResDivisor;
        var targetHeight = height / ResDivisor;

        _aoTarget = CreateTarget(targetWidth, targetHeight);
        _blurTarget = CreateFilteredTarget(targetWidth, targetHeight);

        _history[0] = CreateFilteredTarget(targetWidth, targetHeight);
        _history[1] = CreateFilteredTarget(targetWidth, targetHeight);

        _vao = gl.GenVertexArray();
        _noise = CreateNoise();
    }

    public void Resize(uint width, uint height)
    {
        var targetWidth = width / ResDivisor;
        var targetHeight = height / ResDivisor;

        _aoTarget.Resize(targetWidth, targetHeight);
        _blurTarget.Resize(targetWidth, targetHeight);

        _history[0].Resize(targetWidth, targetHeight);
        _history[1].Resize(targetWidth, targetHeight);

        _hasHistory = false;
    }

    public void Render(uint depthTex, uint normalTex, Camera camera)
    {
        using var _ = Profiling.Tracy.Scope("GTAOPass.Render");

        var proj = camera.GetProjectionMatrix();
        Matrix4x4.Invert(proj, out var invProj);

        var viewProj = camera.GetViewMatrix() * proj;
        Matrix4x4.Invert(viewProj, out var invViewProj);

        ValidateHistory(viewProj);

        _frame++;

        _gl.Disable(EnableCap.DepthTest);

        RenderAmbientOcclusion(proj, invProj, depthTex, normalTex);
        RenderBlur(invProj, depthTex);
        RenderTemporalAccumulation(invProj, invViewProj, depthTex);

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.Enable(EnableCap.DepthTest);

        SwapHistoryBuffers(viewProj);
    }

    private void RenderAmbientOcclusion(Matrix4x4 proj, Matrix4x4 invProj, uint depthTex, uint normalTex)
    {
        _aoTarget.Bind();
        _aoTarget.Clear(1f, 1f, 1f, 1f);

        _gtao.Use();

        _gtao.SetUniform("uProjection", proj);
        _gtao.SetUniform("uInvProjection", invProj);

        _gtao.SetUniform("uRadius", _config.Radius);
        _gtao.SetUniform("uSliceCount", Math.Max(1, _config.SliceCount));
        _gtao.SetUniform("uStepCount", Math.Max(1, _config.StepCount));
        _gtao.SetUniform("uPower", _config.Power);
        _gtao.SetUniform("uFrameIndex", _frame);

        _gtao.SetUniform("uDepth", 0);
        _gtao.SetUniform("uNormal", 1);
        _gtao.SetUniform("uNoise", 2);

        Bind(TextureUnit.Texture0, depthTex);
        Bind(TextureUnit.Texture1, normalTex);
        Bind(TextureUnit.Texture2, _noise);

        DrawFullscreen();
    }

    private void RenderBlur(Matrix4x4 invProj, uint depthTex)
    {
        _blurTarget.Bind();

        _blur.Use();

        _blur.SetUniform("uGtao", 0);
        _blur.SetUniform("uDepth", 1);
        _blur.SetUniform("uInvProjection", invProj);

        Bind(TextureUnit.Texture0, _aoTarget.ColorTextures[0]);
        Bind(TextureUnit.Texture1, depthTex);

        DrawFullscreen();
    }

    private void RenderTemporalAccumulation(Matrix4x4 invProj, Matrix4x4 invViewProj, uint depthTex)
    {
        var write = _history[_write];
        var read = _history[_write ^ 1];

        write.Bind();

        _temporal.Use();

        _temporal.SetUniform("uCurrent", 0);
        _temporal.SetUniform("uHistory", 1);
        _temporal.SetUniform("uDepth", 2);

        _temporal.SetUniform("uInvProjection", invProj);
        _temporal.SetUniform("uInvViewProj", invViewProj);
        _temporal.SetUniform("uPrevViewProj", _prevViewProj);

        _temporal.SetUniform(
            "uFeedback",
            _hasHistory ? _config.TemporalFeedback : 0f);

        Bind(TextureUnit.Texture0, _blurTarget.ColorTextures[0]);
        Bind(TextureUnit.Texture1, read.ColorTextures[0]);
        Bind(TextureUnit.Texture2, depthTex);

        DrawFullscreen();
    }

    private void ValidateHistory(Matrix4x4 viewProj)
    {
        var sliceCount = Math.Max(1, _config.SliceCount);
        var stepCount = Math.Max(1, _config.StepCount);

        var settingsChanged =
            Math.Abs(_config.Radius - _lastRadius) > Tolerance ||
            sliceCount != _lastSliceCount ||
            stepCount != _lastStepCount;

        if (settingsChanged)
        {
            _hasHistory = false;

            _lastRadius = _config.Radius;
            _lastSliceCount = sliceCount;
            _lastStepCount = stepCount;
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

    private GLShader CreateShader(string fragmentShader)
    {
        return new GLShader(
            _gl,
            PathResolver.Resolve("Shaders/Post/post.vert"),
            PathResolver.Resolve($"Shaders/GTAO/{fragmentShader}"));
    }

    private RenderTarget CreateTarget(uint width, uint height)
    {
        return new RenderTarget(_gl, width, height, [InternalFormat.Rgba16f], withDepth: false);
    }

    private RenderTarget CreateFilteredTarget(uint width, uint height)
    {
        return new RenderTarget(_gl, width, height, [InternalFormat.Rgba16f], withDepth: false, filter: GLEnum.Linear);
    }

    private void Bind(TextureUnit unit, uint texture)
    {
        _gl.ActiveTexture(unit);
        _gl.BindTexture(TextureTarget.Texture2D, texture);
    }

    private void DrawFullscreen()
    {
        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        _gl.BindVertexArray(0);
    }

    private unsafe uint CreateNoise()
    {
        var rng = new Random(2);
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

    // ------------------------------------------------------------------------
    // Cleanup
    // ------------------------------------------------------------------------

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