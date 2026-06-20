namespace Centauri.Rendering.Shadows;

using Silk.NET.OpenGL;
using System.Numerics;

using Config;
using World;
using Utils.Misc;
using Graphics.Resources;

// Renders scene depth from the directional light's POV into a ShadowMap and
// exposes the light-space view/projection for the lit pass to sample.
// Single cascade for now; CSM loops this over N cascades.
public sealed class ShadowMapper : IDisposable
{
    private readonly GL _gl;
    private ShadowMap _map;
    private readonly GLShader _depth;

    public bool      Active          { get; private set; }
    public uint      DepthTexture    => _map.DepthTexture;
    public Matrix4x4 LightView       { get; private set; }
    public Matrix4x4 LightProjection { get; private set; }
    public ShadowConfig Config { get; }

    public ShadowMapper(GL gl, ShadowConfig config)
    {
        _gl = gl;
        Config = config;
        _map = new ShadowMap(gl, config.Size);
        _depth = new GLShader(gl,
            PathResolver.Resolve("Assets/Shaders/Shadow/depth.vert"),
            PathResolver.Resolve("Assets/Shaders/Shadow/depth.frag"));
    }

    public void Render(Scene scene)
    {
        Active = false;
        if (!Config.Enabled) return;
        
        if (_map.Size != Config.Size)        // live resolution switch
        {
            _map.Dispose();
            _map = new ShadowMap(_gl, Config.Size);
        }
        
        if (scene.Lighting.DirectionalLights.Count == 0) return;   // collected upstream now

        var dir = Vector3.Normalize(scene.Lighting.DirectionalLights[0].Direction);

        // center the light box on the view camera so shadows follow the player
        var center = scene.Cameras.Active.Position;
        var eye    = center - dir * Config.Distance;

        LightView = Matrix4x4.CreateLookAt(eye, center, Vector3.UnitY);
        LightProjection = Matrix4x4.CreateOrthographic(
            Config.Distance * 2f, Config.Distance * 2f, Config.Near, Config.Far);

        _gl.Disable(EnableCap.CullFace);   // depth from all faces — reduces peter-panning on open meshes
        _map.Bind();

        _depth.Use();
        _depth.SetUniform("uLightView",       LightView);
        _depth.SetUniform("uLightProjection", LightProjection);

        foreach (var entity in scene.Entities)
        {
            if (!entity.Enabled || entity.Model is not { } model) continue;
            _depth.SetUniform("uModel", entity.Transform.WorldMatrix);

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

        _gl.Enable(EnableCap.CullFace);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        Active = true;
    }

    public void Dispose()
    {
        _map.Dispose();
        _depth.Dispose();
    }
}