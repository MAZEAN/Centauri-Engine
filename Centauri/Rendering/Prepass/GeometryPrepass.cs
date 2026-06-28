namespace Centauri.Rendering.Prepass;

using Silk.NET.OpenGL;

using World;
using Graphics.Resources;
using Graphics.Resources.Materials;
using Graphics.Geometry;
using Utils.Misc;
using Targets;
using Config;
using Helper;
using Culling;

// Renders view-space normals + depth + material (roughness/metallic) into single-sample
// textures before the lit pass. These are the inputs the screen-space effects need: SSAO
// reads normals+depth, SSR additionally reads the material buffer to weight reflections.
public sealed class GeometryPrepass : IDisposable
{
    private readonly GL _gl;
    private readonly AppConfig _config;
    private readonly InstanceBuffer _instances;
    
    private readonly GLShader _shader;
    private readonly RenderTarget _target;
    
    private readonly Dictionary<Model, List<InstanceData>> _groups = new();
    // per-model submesh materials (from a representative entity) — drives the foliage alpha test
    private readonly Dictionary<Model, IReadOnlyList<Material?>> _materials = new();

    public uint NormalTexture   => _target.ColorTextures[0];
    public uint MaterialTexture => _target.ColorTextures[1];
    public uint DepthTexture    => _target.DepthTexture;

    public GeometryPrepass(GL gl, AppConfig config, uint width, uint height, InstanceBuffer instances)
    {
        _gl = gl;
        _config = config;
        _instances = instances;
        
        _shader = new GLShader(gl,
            PathResolver.Resolve("Shaders/Prepass/prepass.vert"),
            PathResolver.Resolve("Shaders/Prepass/prepass.frag"));
        _target = new RenderTarget(gl, width, height,
            [InternalFormat.Rgba16f, InternalFormat.Rgba8], withDepth: true);
    }

    public void Resize(uint width, uint height) => _target.Resize(width, height);

    public void Render(Scene scene, CullingSystem culling)
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
        _shader.SetUniform("uAlbedo",       0);
        _shader.SetUniform("uRoughnessMap", 2);   // material buffer (roughness/metallic) for SSR
        _shader.SetUniform("uMetallicMap",  3);
        ShaderUniformBinder.UploadWind(_shader, _config.Wind);

        foreach (var list in _groups.Values)
            list.Clear();

        foreach (var entity in scene.Entities)
        {
            if (!entity.Enabled || entity.Model is not { } model) continue;
            if (!culling.IsVisible(entity)) continue;

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

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    // Alpha-tested foliage (two-sided material with an albedo) discards by alpha so its
    // depth/normals match the leaf cutout, and renders both faces. Everything else is opaque.
    private void SetMeshState(Material? material)
    {
        _shader.SetUniform("uWind", material is { Wind: true } ? 1 : 0);
        BindMaterial(material);
        
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
    
    private void BindMaterial(Material? material)
    {
        if (material is not { } mat)
        {
            _shader.SetUniform("uHasRoughness",   0);
            _shader.SetUniform("uHasMetallic",    0);
            _shader.SetUniform("uRoughnessValue", 1.0f);
            _shader.SetUniform("uMetallicValue",  0.0f);
            return;
        }

        _shader.SetUniform("uHasRoughness",   mat.Roughness != null ? 1 : 0);
        _shader.SetUniform("uHasMetallic",    mat.Metallic  != null ? 1 : 0);
        _shader.SetUniform("uRoughnessValue", mat.RoughnessScalar);
        _shader.SetUniform("uMetallicValue",  mat.MetallicScalar);

        _gl.ActiveTexture(TextureUnit.Texture2);
        _gl.BindTexture(TextureTarget.Texture2D, mat.Roughness?.Handle ?? 0);
        _gl.ActiveTexture(TextureUnit.Texture3);
        _gl.BindTexture(TextureTarget.Texture2D, mat.Metallic?.Handle ?? 0);
    }

    public void Dispose()
    {
        _shader.Dispose();
        _target.Dispose();
    }
}