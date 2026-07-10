namespace Centauri.Rendering.SSAO;

using Silk.NET.OpenGL;
using System.Numerics;

using Config;
using World;
using Graphics.Resources;
using Utils.Misc;
using Targets;

// GTAO: multi-slice horizon search against the prepass depth + view-space normals, then a 4x4
// box blur to remove the noise pattern — see ssao.frag for the algorithm itself. Produces a
// single AO factor the lit pass multiplies into the ambient/IBL term. Class/file/config names
// stayed "SSAO" (the section key in config.json, the AppConfig property, everything downstream
// that binds AoTexture) since from the rest of the engine's perspective it's the same
// abstraction — an AO texture derived from depth+normal — only the technique producing it
// changed, the same way TAA replaced MSAA under RenderConfig without a rename cascading
// through every consumer.
public sealed class SSAOPass : IDisposable
{
    private const int NoiseDim    = 4;
    private const uint ResDivisor = 2;   // half-res

    private readonly GL _gl;
    private readonly SSAOConfig _config;
    
    private readonly GLShader _ssao;
    private readonly GLShader _blur;
    private readonly uint _vao;     // empty VAO for attribute-less fullscreen draws
    private readonly uint _noise;

    private readonly RenderTarget _aoTarget;
    private readonly RenderTarget _blurTarget;

    public uint AoTexture => _blurTarget.ColorTextures[0];

    public SSAOPass(GL gl, SSAOConfig config, uint width, uint height)
    {
        _gl = gl;
        _config = config;

        _ssao = new GLShader(gl,
            PathResolver.Resolve("Shaders/Post/post.vert"),
            PathResolver.Resolve("Shaders/SSAO/ssao.frag"));
        _blur = new GLShader(gl,
            PathResolver.Resolve("Shaders/Post/post.vert"),
            PathResolver.Resolve("Shaders/SSAO/ssao_blur.frag"));
        _aoTarget   = new RenderTarget(gl, width / ResDivisor, height / ResDivisor,
            [InternalFormat.Rgba16f], withDepth: false);
        _blurTarget = new RenderTarget(gl, width / ResDivisor, height / ResDivisor,
            [InternalFormat.Rgba16f], withDepth: false, filter: GLEnum.Linear);

        _vao   = gl.GenVertexArray();
        _noise = CreateNoise();
    }

    public void Resize(uint width, uint height)
    {
        _aoTarget.Resize(width / ResDivisor, height / ResDivisor);
        _blurTarget.Resize(width / ResDivisor, height / ResDivisor);
    }

    public void Render(uint depthTex, uint normalTex, Camera camera)
    {
        var proj = camera.GetProjectionMatrix();
        Matrix4x4.Invert(proj, out var invProj);

        _gl.Disable(EnableCap.DepthTest);

        // ── occlusion ──
        _aoTarget.Bind();
        _aoTarget.Clear(1f, 1f, 1f, 1f);

        _ssao.Use();
        _ssao.SetUniform("uProjection",    proj);
        _ssao.SetUniform("uInvProjection", invProj);
        _ssao.SetUniform("uRadius",     _config.Radius);
        _ssao.SetUniform("uSliceCount", Math.Max(1, _config.SliceCount));
        _ssao.SetUniform("uStepCount",  Math.Max(1, _config.StepCount));
        _ssao.SetUniform("uPower",      _config.Power);

        _ssao.SetUniform("uDepth",  0);
        _ssao.SetUniform("uNormal", 1);
        _ssao.SetUniform("uNoise",  2);
        
        Bind(TextureUnit.Texture0, depthTex);
        Bind(TextureUnit.Texture1, normalTex);
        Bind(TextureUnit.Texture2, _noise);
        DrawFullscreen();

        // ── blur ──
        _blurTarget.Bind();
        
        _blur.Use();
        _blur.SetUniform("uSsao", 0);
        
        Bind(TextureUnit.Texture0, _aoTarget.ColorTextures[0]);
        DrawFullscreen();

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.Enable(EnableCap.DepthTest);
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
        _ssao.Dispose();
        _blur.Dispose();
        _aoTarget.Dispose();
        _blurTarget.Dispose();
        _gl.DeleteTexture(_noise);
        _gl.DeleteVertexArray(_vao);
    }
}
