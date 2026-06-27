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

internal readonly struct Caster
{
    public readonly InstanceData Data;
    public readonly BoundingBox  Bounds;

    public Caster(InstanceData data, BoundingBox bounds)
    {
        Data = data; 
        Bounds = bounds;
    }
}

public sealed class ShadowMapper : IDisposable
{
    private const float SlopeBias    = 2.0f;   // polygon-offset factor
    private const float ConstantBias = 4.0f;   // polygon-offset units
    
    private readonly GL _gl;
    private readonly AppConfig _config;
    private readonly InstanceBuffer _instances;
    
    private ShadowArray _maps;
    private readonly GLShader _depth;
    private readonly Frustum _cull = new();
    private readonly CascadeBuilder _cascadeBuilder;
    
    private readonly Dictionary<Model, List<Caster>> _solid    = new();
    private readonly Dictionary<Model, List<Caster>> _twoSided = new();
    private readonly Dictionary<Model, IReadOnlyList<Material?>> _materials = new();
    private readonly List<InstanceData> _visible = new(); 
    
    private int         _boundsRevision = -1;
    private BoundingBox _cachedSceneBounds;

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
        var sceneBounds = SceneBounds(scene);

        Cascades = _cascadeBuilder.Build(camera, dir, sceneBounds, Cascades);
        
        CollectCasters(scene);

        SetRenderState();
        _depth.Use();
        
        _depth.SetUniform("uAlbedo", 0);
        _depth.SetUniform("uTime", Time.Now);
        
        for (var c = 0; c < Cascades.Length; c++)
        {
            _maps.BindLayer(c);
            _depth.SetUniform("uLightMatrix", Cascades[c].Matrix);
            
            _cull.Update(Cascades[c].Matrix);
            
            SetSolidRenderState();
            DrawGroups(_solid, ref stats);
            ResetSolidRenderState();
            
            DrawGroups(_twoSided, ref stats);
        }

        ResetRenderState();
        Active = true;
    }
    
    private void CollectCasters(Scene scene)
    {
        foreach (var list in _solid.Values)    list.Clear();
        foreach (var list in _twoSided.Values) list.Clear();

        foreach (var entity in scene.Entities)
        {
            if (!entity.Enabled || entity.Model is not { } model)
                continue;

            var groups = entity.AnyTwoSided ? _twoSided : _solid;
            if (!groups.TryGetValue(model, out var list))
                groups[model] = list = new List<Caster>();
            
            _materials[model] = entity.Materials;
            list.Add(new Caster(
                new InstanceData(entity.Transform.WorldMatrix, entity.UvScale, entity.UvOffset),
                entity.GetWorldBounds()));
        }
    }
    
    private void DrawGroups(Dictionary<Model, List<Caster>> groups, ref FrameStats stats)
    {
        foreach (var (model, casters) in groups)
        {
            if (casters.Count == 0) continue;
            
            _visible.Clear();
            foreach (var caster in casters)
            {
                if (_cull.IsVisibleAABB(caster.Bounds))
                    _visible.Add(caster.Data);
                else
                    stats.ShadowCulled++;
            }

            if (_visible.Count == 0) continue;

            _instances.Upload(_visible);
            stats.ShadowCasters += _visible.Count;

            var materials = _materials[model];
            for (var i = 0; i < model.Meshes.Count; i++)
            {
                SetCasterAlphaTest(i < materials.Count ? materials[i] : null);

                var mesh = model.Meshes[i];
                mesh.ConfigureInstancing(_instances.Handle);
                mesh.DrawInstanced(_visible.Count);
            }
        }
    }
    
    private void SetCasterAlphaTest(Material? material)
    {
        _depth.SetUniform("uWind", material is { Wind: true } ? 1 : 0);
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
    
    private BoundingBox SceneBounds(Scene scene)
    {
        if (scene.Revision != _boundsRevision)
        {
            _cachedSceneBounds = ComputeSceneBounds(scene);
            _boundsRevision    = scene.Revision;
        }
        return _cachedSceneBounds;
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

    private void SetSolidRenderState()
    {
        _gl.Enable(EnableCap.CullFace);
        _gl.CullFace(TriangleFace.Front);
        _gl.Enable(EnableCap.PolygonOffsetFill);
        _gl.PolygonOffset(SlopeBias, ConstantBias);
    }

    private void ResetSolidRenderState()
    {
        _gl.Disable(EnableCap.PolygonOffsetFill);
        _gl.Disable(EnableCap.CullFace);
    }

    private void SetRenderState()
    {
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