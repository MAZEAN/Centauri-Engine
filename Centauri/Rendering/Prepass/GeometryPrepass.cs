namespace Centauri.Rendering.Prepass;

using Silk.NET.OpenGL;

using World;
using Graphics.Resources;
using Graphics.Geometry;
using Utils.Misc;
using Targets;
using Config;

// Renders view-space normals + depth + material (roughness/metallic) into single-sample
// textures before the lit pass. These are the inputs the screen-space effects need: SSAO
// reads normals+depth, SSR additionally reads the material buffer to weight reflections.
public sealed class GeometryPrepass : IDisposable
{
    private readonly GL _gl;
    private readonly AppConfig _config;
    private readonly GLShader _shader;
    private readonly RenderTarget _target;
    private readonly InstanceBuffer _instances;
    
    private readonly Dictionary<Model, List<InstanceData>> _groups = new();

    public uint NormalTexture   => _target.ColorTextures[0];
    public uint DepthTexture    => _target.DepthTexture;

    public GeometryPrepass(GL gl, AppConfig config, uint width, uint height, InstanceBuffer instances)
    {
        _gl = gl;
        _config = config;
        _instances = instances;
        _shader = new GLShader(gl,
            PathResolver.Resolve("Assets/Shaders/Prepass/prepass.vert"),
            PathResolver.Resolve("Assets/Shaders/Prepass/prepass.frag"));
        _target = new RenderTarget(gl, width, height, [InternalFormat.Rgba16f], withDepth: true);
    }

    public void Resize(uint width, uint height) => _target.Resize(width, height);

    public void Render(Scene scene)
    {
        var camera = scene.Cameras.Active;
        
        var cullingCamera = scene.Cameras.Primary;
        cullingCamera.UpdateFrustum();
        var cull = _config.Debug.EnableCulling;

        _target.Bind();
        _target.Clear();

        _gl.Enable(EnableCap.DepthTest);
        _gl.Enable(EnableCap.CullFace);
        _gl.CullFace(TriangleFace.Back);

        _shader.Use();
        _shader.SetUniform("uView",       camera.GetViewMatrix());
        _shader.SetUniform("uProjection", camera.GetProjectionMatrix());
        
        foreach (var list in _groups.Values)
            list.Clear();

        foreach (var entity in scene.Entities)
        {
            if (!entity.Enabled || entity.Model is not { } model) continue;
            if (cull && !cullingCamera.Frustum.IsVisibleAABB(entity.GetWorldBounds()))
                continue;

            if (!_groups.TryGetValue(model, out var list))
                _groups[model] = list = new List<InstanceData>();

            list.Add(new InstanceData(entity.Transform.WorldMatrix, entity.UvScale, entity.UvOffset));
        }
        
        foreach (var (model, list) in _groups)
        {
            if (list.Count == 0) continue;

            _instances.Upload(list);
            foreach (var mesh in model.Meshes)
            {
                mesh.ConfigureInstancing(_instances.Handle);
                mesh.DrawInstanced(list.Count);
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