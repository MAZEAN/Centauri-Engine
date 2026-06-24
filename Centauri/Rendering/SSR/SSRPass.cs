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
// (roughness/metallic) and Fresnel. The result is an additive contribution the post stack
// folds into the scene before tonemapping. See Assets/Shaders/SSR/ssr.frag for the march.
public sealed class SSRPass : IDisposable
{
    private readonly GL _gl;
    private readonly SSRConfig _config;
    private readonly GLShader _shader;
    private readonly uint _vao;

    private RenderTarget _target;

    public uint ReflectionTexture => _target.ColorTextures[0];

    public SSRPass(GL gl, SSRConfig config, uint width, uint height)
    {
        _gl = gl;
        _config = config;
        _shader = new GLShader(gl,
            PathResolver.Resolve("Assets/Shaders/Post/post.vert"),
            PathResolver.Resolve("Assets/Shaders/SSR/ssr.frag"));
        // Linear so the additive composite upsamples smoothly if we ever run sub-res
        _target = new RenderTarget(gl, width, height, [InternalFormat.Rgba16f],
            withDepth: false, filter: GLEnum.Linear);
        _vao = gl.GenVertexArray();
    }

    public void Resize(uint width, uint height) => _target.Resize(width, height);

    public void Render(uint sceneTex, uint depthTex, uint normalTex, uint materialTex, Camera camera)
    {
        var proj = camera.GetProjectionMatrix();
        Matrix4x4.Invert(proj, out var invProj);

        _gl.Disable(EnableCap.DepthTest);

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

        _shader.SetUniform("uScene",    0);
        _shader.SetUniform("uDepth",    1);
        _shader.SetUniform("uNormal",   2);
        _shader.SetUniform("uMaterial", 3);
        Bind(TextureUnit.Texture0, sceneTex);
        Bind(TextureUnit.Texture1, depthTex);
        Bind(TextureUnit.Texture2, normalTex);
        Bind(TextureUnit.Texture3, materialTex);

        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        _gl.BindVertexArray(0);

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.Enable(EnableCap.DepthTest);
    }

    private void Bind(TextureUnit unit, uint tex)
    {
        _gl.ActiveTexture(unit);
        _gl.BindTexture(TextureTarget.Texture2D, tex);
    }

    public void Dispose()
    {
        _shader.Dispose();
        _target.Dispose();
        _gl.DeleteVertexArray(_vao);
    }
}
