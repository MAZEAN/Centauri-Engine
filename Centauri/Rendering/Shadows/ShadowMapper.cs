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
using Helper;
using Culling;

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
    
    private readonly Dictionary<Model, List<InstanceData>> _solid    = new();
    private readonly Dictionary<Model, List<InstanceData>> _twoSided = new();
    private readonly Dictionary<Model, IReadOnlyList<Material?>> _materials = new();
    private readonly HashSet<Entity> _cascadeVisible = new();
    
    private int         _boundsRevision = -1;
    private BoundingBox _cachedSceneBounds;
    private bool        _sceneHasWind;
    
    private readonly ShadowCache _cache = new();

    public bool Active { get; private set; }
    public uint DepthTexture    => _maps.DepthTexture;
    public uint RawDepthTexture => _maps.RawDepthTexture;

    public Cascade[] Cascades { get; private set; } = [];

    public ShadowMapper(GL gl, AppConfig config, InstanceBuffer instances)
    {
        _gl = gl;
        _config = config;
        _instances = instances;
        
        _cascadeBuilder = new CascadeBuilder(config);
        _maps = new ShadowArray(gl, config.Shadows.Size, config.Shadows.MaxCascades);
        _depth = new GLShader(gl,
            PathResolver.Resolve("Shaders/Shadow/depth.vert"),
            PathResolver.Resolve("Shaders/Shadow/depth.frag"));
    }

    public void Render(Scene scene, CullingSystem culling, ref FrameStats stats)
    {
        stats.ShadowCasters = 0;
        stats.ShadowCulled  = 0;

        Active = false;
        if (!_config.Shadows.Enabled) return;

        if (_maps.Size != _config.Shadows.Size)
        {
            _maps.Dispose();
            _maps = new ShadowArray(_gl, _config.Shadows.Size, _config.Shadows.MaxCascades);
            _cache.Invalidate();   // fresh, empty maps — must redraw
        }

        if (scene.Lighting.DirectionalLights.Count == 0) return;

        var dir         = Vector3.Normalize(scene.Lighting.DirectionalLights[0].Direction);
        var camera      = scene.Cameras.Active;
        var sceneBounds = SceneBounds(scene);   // also refreshes _sceneHasWind (revision-gated)

        var s   = _config.Shadows;
        var key = new ShadowCacheKey(dir, camera.GetViewMatrix() * camera.GetProjectionMatrix(),
            scene.Revision, s.CascadeCount, s.Distance, s.SplitLambda);

        if (_cache.CanReuse(key, _sceneHasWind && _config.Wind.Animating))
        {
            Active = true;   // last frame's depth maps + Cascades are still valid
            return;
        }

        Cascades = _cascadeBuilder.Build(camera, dir, sceneBounds, Cascades);


        SetRenderState();
        _depth.Use();
        
        _depth.SetUniform("uAlbedo", 0);
        ShaderUniformBinder.UploadWind(_depth, _config.Wind);
        
        var totalCasters = culling.EntityCount;
        for (var c = 0; c < Cascades.Length; c++)
        {
            _maps.BindLayer(c);
            _depth.SetUniform("uLightMatrix", Cascades[c].Matrix);
            
            _cull.Update(Cascades[c].Matrix);
            
            _cascadeVisible.Clear();
            culling.CullInto(_cull, _cascadeVisible);
            stats.ShadowCulled += totalCasters - _cascadeVisible.Count;

            BucketCasters(_cascadeVisible);
            
            SetSolidRenderState();
            DrawGroups(_solid, ref stats);
            ResetSolidRenderState();
            
            DrawGroups(_twoSided, ref stats);
        }

        ResetRenderState();
        
        _maps.SyncRawDepth();
        Active = true;

        _cache.Record(key);   // these maps are valid until one of the key's inputs changes
    }

    private void BucketCasters(HashSet<Entity> visible)
    {
        foreach (var list in _solid.Values)    
            list.Clear();
        foreach (var list in _twoSided.Values) 
            list.Clear();

        foreach (var entity in visible)
        {
            if (entity.Model is not { } model) continue;

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
            _sceneHasWind      = ComputeHasWind(scene);
            _boundsRevision    = scene.Revision;
        }
        return _cachedSceneBounds;
    }
    
    private static bool ComputeHasWind(Scene scene)
    {
        foreach (var e in scene.Entities)
        {
            if (!e.Enabled || e.Model is null) continue;
            foreach (var m in e.Materials)
                if (m is { Wind: true }) return true;
        }
        return false;
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