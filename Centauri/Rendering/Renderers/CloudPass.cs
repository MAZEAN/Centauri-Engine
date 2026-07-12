namespace Centauri.Rendering.Renderers;

using Silk.NET.OpenGL;
using System.Numerics;

using World;
using Config;
using Graphics.Resources;
using Graphics.Geometry;
using Utils.Misc;
using Targets;

// Clouds are the expensive part of the sky shader — repeated fbm noise evaluations per pixel.
// Rendered here into a quarter-area (half linear resolution) offscreen target using the same
// direction-mapping cube + vertex shader as SkyboxRenderer, then sampled back (bilinear-
// upscaled) by skybox.frag. Clouds are inherently soft, low-frequency shapes, so the resolution
// loss is invisible while the noise cost drops ~4x.
public sealed class CloudPass : IDisposable
{
    private const uint ResDivisor = 2;

    private readonly GL _gl;
    private readonly SkyConfig _config;
    private readonly GLShader _shader;
    private readonly Mesh _cube;

    private readonly RenderTarget _target;

    public uint CloudTexture => _target.ColorTextures[0];
    public bool Active { get; private set; }

    public CloudPass(GL gl, SkyConfig config, Mesh cube, uint width, uint height)
    {
        _gl = gl;
        _config = config;
        _cube = cube;

        _shader = new GLShader(gl,
            PathResolver.Resolve("Shaders/Sky/skybox.vert"),
            PathResolver.Resolve("Shaders/Sky/clouds.frag"));

        _target = new RenderTarget(gl, Size(width), Size(height),
            [InternalFormat.Rgba16f], withDepth: false, filter: GLEnum.Linear);
    }

    public void Resize(uint width, uint height) => _target.Resize(Size(width), Size(height));

    private static uint Size(uint v) => Math.Max(1u, v / ResDivisor);

    public void Render(Scene scene, Matrix4x4 view, Matrix4x4 projection)
    {
        using var _ = Profiling.Tracy.Scope("CloudPass.Render");

        Active = _config.Clouds && _config.CloudCoverage > 0f;
        if (!Active) return;

        view.Translation = Vector3.Zero;

        _target.Bind();
        _target.Clear(0f, 0f, 0f, 0f);

        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.CullFace);

        _shader.Use();
        _shader.SetUniform("uView",          view);
        _shader.SetUniform("uProjection",    projection);
        _shader.SetUniform("uCloudCoverage", _config.CloudCoverage);
        _shader.SetUniform("uCloudScale",    _config.CloudScale);
        _shader.SetUniform("uCloudSpeed",    _config.CloudSpeed);
        _shader.SetUniform("uCloudShading",  _config.CloudShading);
        _shader.SetUniform("uTime",          Time.Now);

        UploadSun(scene);

        _cube.Bind();
        unsafe
        {
            _gl.DrawElements(PrimitiveType.Triangles, _cube.IndexCount,
                DrawElementsType.UnsignedInt, (void*)0);
        }

        _gl.Enable(EnableCap.CullFace);
        _gl.Enable(EnableCap.DepthTest);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    private void UploadSun(Scene scene)
    {
        var sunDir = scene.Lighting.DirectionalLights.Count > 0
            ? -Vector3.Normalize(scene.Lighting.DirectionalLights[0].Direction)
            : Vector3.UnitY;

        _shader.SetUniform("uSunDir", sunDir);
    }

    public void Dispose()
    {
        _shader.Dispose();
        _target.Dispose();
    }
}
