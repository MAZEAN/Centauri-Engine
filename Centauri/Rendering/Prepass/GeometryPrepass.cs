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
// textures before the lit pass. These are the inputs the screen-space effects need: GTAO
// reads normals+depth, SSR additionally reads the material buffer to weight reflections.
// The depth uses the exact same alpha-cutout as the lit pass (see prepass.frag), so it's also
// trustworthy for Forward's own early-Z when RenderingSystem borrows it instead of running
// ZPrepass's separate depth-only draw — see HDRFramebuffer.TryBorrowDepth.
public sealed class GeometryPrepass : IDisposable
{
    private readonly GL _gl;
    private readonly AppConfig _config;
    private readonly InstanceBuffer _instances;
    
    private readonly GLShader _shader;
    private readonly RenderTarget _target;
    
    private readonly ShaderBatcher _batcher = new();
    private readonly List<InstanceData> _instanceData = [];
    
    private bool? _cullEnabled;

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
        using var _ = Profiling.Tracy.Scope("GeometryPrepass.Render");

        BeginPass(scene);

        foreach (var batch in _batcher.GetBatches(scene))
            RenderBatch(batch, culling);
        
        EndPass();
    }

    private void BeginPass(Scene scene)
    {
        var camera = scene.Cameras.Active;

        _target.Bind();
        _target.Clear();

        SetRenderState();

        _shader.Use();
        _shader.SetUniform("uView",       camera.GetViewMatrix());
        _shader.SetUniform("uProjection", camera.GetProjectionMatrix());
        _shader.SetUniform("uAlbedo",       0);
        _shader.SetUniform("uAOMap",        1);
        _shader.SetUniform("uRoughnessMap", 2);
        _shader.SetUniform("uMetallicMap",  3);
        _shader.SetUniform("uFoliageAlphaCutoff", _config.Foliage.AlphaCutoff);
        ShaderUniformBinder.UploadWind(_shader, _config.Foliage);
    }

    private void RenderBatch(Batch batch, CullingSystem culling)
    {
        _instanceData.Clear();
        foreach (var entity in batch.Entities)
        {
            if (!entity.Enabled || !culling.IsVisible(entity)) continue;

            _instanceData.Add(new InstanceData(entity.Transform.WorldMatrix, entity.UvScale, entity.UvOffset));
        }
        
        if (_instanceData.Count == 0) return;
        _instances.Upload(_instanceData);

        var meshes = batch.Model.Meshes;
        for (var i = 0; i < meshes.Count; i++)
        {
            SetMeshState(i < batch.Materials.Length ? batch.Materials[i] : null);

            var mesh = meshes[i];
            mesh.ConfigureInstancing(_instances.Handle);
            mesh.DrawInstanced(_instanceData.Count);
        }
    }

    private void EndPass()
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }
    
    private void SetMeshState(Material? material)
    {
        _shader.SetUniform("uWind", material is { Wind: true } ? 1 : 0);
        BindMaterial(material);
        
        if (material is { TwoSided: true, Albedo: { } albedo })
        {
            _shader.SetUniform("uAlphaTest", 1);
            _gl.ActiveTexture(TextureUnit.Texture0);
            _gl.BindTexture(TextureTarget.Texture2D, albedo.Handle);
            SetCullState(false);
        }
        else
        {
            _shader.SetUniform("uAlphaTest", 0);
            SetCullState(true);
        }
    }
    
    private void BindMaterial(Material? material)
    {
        if (material is null)
        {
            _shader.SetUniform("uHasRoughness",   0);
            _shader.SetUniform("uHasMetallic",    0);
            _shader.SetUniform("uHasAO",          0);
            _shader.SetUniform("uRoughnessValue", 1.0f);
            _shader.SetUniform("uMetallicValue",  0.0f);
            return;
        }

        _shader.SetUniform("uHasRoughness",   material.Roughness != null ? 1 : 0);
        _shader.SetUniform("uHasMetallic",    material.Metallic  != null ? 1 : 0);
        _shader.SetUniform("uHasAO",          material.AO        != null ? 1 : 0);
        _shader.SetUniform("uRoughnessValue", material.RoughnessScalar);
        _shader.SetUniform("uMetallicValue",  material.MetallicScalar);

        _gl.ActiveTexture(TextureUnit.Texture1);
        _gl.BindTexture(TextureTarget.Texture2D, material.AO?.Handle ?? 0);
        _gl.ActiveTexture(TextureUnit.Texture2);
        _gl.BindTexture(TextureTarget.Texture2D, material.Roughness?.Handle ?? 0);
        _gl.ActiveTexture(TextureUnit.Texture3);
        _gl.BindTexture(TextureTarget.Texture2D, material.Metallic?.Handle ?? 0);
    }
    
    private void SetCullState(bool enabled)
    {
        if (_cullEnabled == enabled) return;
        _cullEnabled = enabled;

        if (enabled) _gl.Enable(EnableCap.CullFace);
        else         _gl.Disable(EnableCap.CullFace);
    }

    private void SetRenderState()
    {
        _gl.Enable(EnableCap.DepthTest);
        _gl.CullFace(TriangleFace.Back);
        _cullEnabled = null;
        SetCullState(true);
    }

    public void Dispose()
    {
        _shader.Dispose();
        _target.Dispose();
    }
}