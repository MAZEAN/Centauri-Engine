namespace Centauri.Rendering.Prepass;

using Silk.NET.OpenGL;

using World;
using Graphics.Resources;
using Graphics.Resources.Materials;
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
    // per-model submesh materials (from a representative entity) — drives the foliage alpha test
    private readonly Dictionary<Model, IReadOnlyList<Material?>> _materials = new();

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
        _shader.SetUniform("uAlbedo",     0);

        foreach (var list in _groups.Values)
            list.Clear();

        foreach (var entity in scene.Entities)
        {
            if (!entity.Enabled || entity.Model is not { } model) continue;
            if (cull && !cullingCamera.Frustum.IsVisibleAABB(entity.GetWorldBounds()))
                continue;

            if (!_groups.TryGetValue(model, out var list))
                _groups[model] = list = new List<InstanceData>();

            _materials[model] = entity.Materials;   // representative — foliage materials are shared
            list.Add(new InstanceData(entity.Transform.WorldMatrix, entity.UvScale, entity.UvOffset));
        }

        foreach (var (model, list) in _groups)
        {
            if (list.Count == 0) continue;

            _instances.Upload(list);
            var materials = _materials[model];

            for (var i = 0; i < model.Meshes.Count; i++)
            {
                SetMeshState(i < materials.Count ? materials[i] : null);

                var mesh = model.Meshes[i];
                mesh.ConfigureInstancing(_instances.Handle);
                mesh.DrawInstanced(list.Count);
            }
        }

        _gl.Enable(EnableCap.CullFace);   // restore default for the next pass
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    // Alpha-tested foliage (two-sided material with an albedo) discards by alpha so its
    // depth/normals match the leaf cutout, and renders both faces. Everything else is opaque.
    private void SetMeshState(Material? material)
    {
        if (material is { TwoSided: true, Albedo: { } albedo })
        {
            _shader.SetUniform("uAlphaTest", 1);
            _gl.ActiveTexture(TextureUnit.Texture0);
            _gl.BindTexture(TextureTarget.Texture2D, albedo.Handle);
            _gl.Disable(EnableCap.CullFace);
        }
        else
        {
            _shader.SetUniform("uAlphaTest", 0);
            _gl.Enable(EnableCap.CullFace);
        }
    }

    public void Dispose()
    {
        _shader.Dispose();
        _target.Dispose();
    }
}