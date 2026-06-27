namespace Centauri.Rendering.Shadows;

using Silk.NET.OpenGL;
using System.Numerics;

using Config;
using World;
using Utils.Misc;
using Graphics.Resources;
using Graphics.Resources.Materials;
using Graphics.Geometry;
using Utils.Geometry;

public sealed class ShadowMapper : IDisposable
{
    private readonly GL _gl;
    private readonly AppConfig _config;
    private readonly InstanceBuffer _instances;
    
    private ShadowArray _maps;
    private readonly GLShader _depth;
    private readonly Frustum _cull = new();
    private readonly CascadeBuilder _cascadeBuilder;
    
    private readonly Dictionary<Model, List<InstanceData>> _solid    = new();
    private readonly Dictionary<Model, List<InstanceData>> _twoSided = new();
    private readonly Dictionary<Model, IReadOnlyList<Material?>> _materials = new();

    public bool Active { get; private set; }
    public uint DepthTexture => _maps.DepthTexture;

    public Cascade[] Cascades { get; private set; } = [];

    public ShadowMapper(GL gl, AppConfig config, InstanceBuffer instances)
    {
        _gl = gl;
        _config = config;
        _instances = instances;
        
        _cascadeBuilder = new CascadeBuilder(config);
        _maps = new ShadowArray(gl, config.Shadows.Size, config.Shadows.MaxCascades);
        _depth = new GLShader(gl,
            PathResolver.Resolve("Assets/Shaders/Shadow/depth.vert"),
            PathResolver.Resolve("Assets/Shaders/Shadow/depth.frag"));
    }

    public void Render(Scene scene, ref FrameStats stats)
    {
        stats.ShadowCasters = 0;
        stats.ShadowCulled  = 0;

        Active = false;
        if (!_config.Shadows.Enabled) return;

        if (_maps.Size != _config.Shadows.Size)
        {
            _maps.Dispose();
            _maps = new ShadowArray(_gl, _config.Shadows.Size, _config.Shadows.MaxCascades);
        }

        if (scene.Lighting.DirectionalLights.Count == 0) return;

        var dir         = Vector3.Normalize(scene.Lighting.DirectionalLights[0].Direction);
        var camera      = scene.Cameras.Active;
        var sceneBounds = ComputeSceneBounds(scene);

        Cascades = _cascadeBuilder.Build(camera, dir, sceneBounds, Cascades);

        SetRenderState();
        
        for (var c = 0; c < Cascades.Length; c++)
        {
            _maps.BindLayer(c);
            _depth.Use();
            _depth.SetUniform("uLightMatrix", Cascades[c].Matrix);
            _depth.SetUniform("uAlbedo", 0);
            
            _cull.Update(Cascades[c].Matrix);
            
            BucketCasters(scene, ref stats);
            
            _gl.Enable(EnableCap.CullFace);
            _gl.CullFace(TriangleFace.Front);
            DrawGroups(_solid, ref stats);
            
            _gl.Disable(EnableCap.CullFace);
            DrawGroups(_twoSided, ref stats);
        }

        ResetRenderState();
        Active = true;
    }
    
    private void BucketCasters(Scene scene, ref FrameStats stats)
    {
        foreach (var list in _solid.Values)    list.Clear();
        foreach (var list in _twoSided.Values) list.Clear();

        foreach (var entity in scene.Entities)
        {
            if (!entity.Enabled || entity.Model is not { } model)
                continue;

            if (!_cull.IsVisibleAABB(entity.GetWorldBounds()))
            {
                stats.ShadowCulled++;
                continue;
            }

            var groups = entity.AnyTwoSided ? _twoSided : _solid;
            if (!groups.TryGetValue(model, out var list))
                groups[model] = list = new List<InstanceData>();
            
            _materials[model] = entity.Materials;
            list.Add(new InstanceData(entity.Transform.WorldMatrix, entity.UvScale, entity.UvOffset));
        }
    }
    
    private void DrawGroups(Dictionary<Model, List<InstanceData>> groups, ref FrameStats stats)
    {
        foreach (var (model, list) in groups)
        {
            if (list.Count == 0) continue;

            _instances.Upload(list);
            stats.ShadowCasters += list.Count;

            var materials = _materials[model];
            for (var i = 0; i < model.Meshes.Count; i++)
            {
                SetCasterAlphaTest(i < materials.Count ? materials[i] : null);

                var mesh = model.Meshes[i];
                mesh.ConfigureInstancing(_instances.Handle);
                mesh.DrawInstanced(list.Count);
            }
        }
    }
    
    private void SetCasterAlphaTest(Material? material)
    {
        if (material is { TwoSided: true, Albedo: { } albedo })
        {
            _depth.SetUniform("uAlphaTest", 1);
            _gl.ActiveTexture(TextureUnit.Texture0);
            _gl.BindTexture(TextureTarget.Texture2D, albedo.Handle);
        }
        else
        {
            _depth.SetUniform("uAlphaTest", 0);
        }
    }
    
    private static BoundingBox ComputeSceneBounds(Scene scene)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        foreach (var e in scene.Entities)
        {
            if (!e.Enabled || e.Model is null) continue;
            var b = e.GetWorldBounds();
            min = Vector3.Min(min, b.Min);
            max = Vector3.Max(max, b.Max);
        }

        return min.X <= max.X ? new BoundingBox(min, max)
            : new BoundingBox(Vector3.Zero, Vector3.Zero);   // no casters
    }

    private void SetRenderState()
    {
        _gl.Enable(EnableCap.PolygonOffsetFill);
        _gl.Enable(EnableCap.CullFace);
    }

    private void ResetRenderState()
    {
        _gl.Disable(EnableCap.PolygonOffsetFill);
        _gl.CullFace(TriangleFace.Back);
        _gl.Enable(EnableCap.CullFace);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }


    public void Dispose()
    {
        _maps.Dispose();
        _depth.Dispose();
    }
}