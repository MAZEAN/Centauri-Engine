namespace Centauri.Rendering.Prepass;

using Silk.NET.OpenGL;
using System.Numerics;

using World;
using Graphics.Resources;
using Utils.Misc;
using Targets;

// Renders view-space normals + depth into single-sample textures before the lit pass.
// These are the inputs the screen-space effects need (SSAO first, then SSR). Nothing
// consumes them yet — this is the Phase 1 keystone the rest of the track builds on.
public sealed class GeometryPrepass : IDisposable
{
    private readonly GL _gl;
    private readonly GLShader _shader;
    private readonly RenderTarget _target;

    public uint NormalTexture => _target.ColorTextures[0];
    public uint DepthTexture  => _target.DepthTexture;

    public GeometryPrepass(GL gl, uint width, uint height)
    {
        _gl = gl;
        _shader = new GLShader(gl,
            PathResolver.Resolve("Assets/Shaders/Prepass/prepass.vert"),
            PathResolver.Resolve("Assets/Shaders/Prepass/prepass.frag"));
        _target = new RenderTarget(gl, width, height, [InternalFormat.Rgba16f], withDepth: true);
    }

    public void Resize(uint width, uint height) => _target.Resize(width, height);

    public void Render(Scene scene)
    {
        var camera = scene.Cameras.Active;

        _target.Bind();
        _target.Clear();

        _gl.Enable(EnableCap.DepthTest);
        _gl.Enable(EnableCap.CullFace);
        _gl.CullFace(TriangleFace.Back);

        _shader.Use();
        _shader.SetUniform("uView",       camera.GetViewMatrix());
        _shader.SetUniform("uProjection", camera.GetProjectionMatrix());

        foreach (var entity in scene.Entities)
        {
            if (!entity.Enabled || entity.Model is not { } model) continue;

            var world = entity.Transform.WorldMatrix;
            _shader.SetUniform("uModel", world);
            _shader.SetUniformMat3X3("uNormalMatrix",
                Matrix4x4.Transpose(Matrix4x4.Invert(world, out var inv) ? inv : world));

            foreach (var mesh in model.Meshes)
            {
                mesh.Bind();
                unsafe
                {
                    _gl.DrawElements(PrimitiveType.Triangles, mesh.IndexCount,
                        DrawElementsType.UnsignedInt, (void*)0);
                }
            }
        }

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public void Dispose()
    {
        _shader.Dispose();
        _target.Dispose();
    }
}