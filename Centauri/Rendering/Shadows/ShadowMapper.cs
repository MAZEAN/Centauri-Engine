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
    private readonly Profiling.GPUProfiler _profiler;

    // Two resolution tiers: cascade 0 (the near, tightest-fit slice) always renders at the full
    // configured Size; every other cascade shares a lower-resolution array (FarCascadeScale) —
    // they cover a much larger world-space area at the same physical resolution already, so
    // their texel density is inherently lower, and matching the near cascade's resolution just
    // spends fill-rate/memory on detail that was never there. See ShadowConfig.FarCascadeScale.
    private ShadowArray _mapsNear;
    private ShadowArray _mapsFar;
    private readonly GLShader _depth;
    private readonly Frustum _cull = new();
    private readonly CascadeBuilder _cascadeBuilder;
    
    // Per-cascade caster buckets, filled by a cull+bucket pre-pass before any drawing starts —
    // one fixed slot per possible cascade (MaxCascades), so this never resizes at runtime. This
    // lets the actual draws below be reordered into two GPU-contiguous passes (all cascades'
    // opaque casters, then all cascades' alpha-tested foliage casters) instead of interleaving
    // the two per cascade, so each category gets its own GL_TIME_ELAPSED zone — see Render().
    private readonly Dictionary<Model, List<InstanceData>>[] _solidByCascade;
    private readonly Dictionary<Model, List<InstanceData>>[] _twoSidedByCascade;
    private readonly Dictionary<Model, IReadOnlyList<Material?>> _materials = new();
    private readonly HashSet<Entity> _cascadeVisible = new();
    
    private int         _boundsRevision = -1;
    private BoundingBox _cachedSceneBounds;
    private bool        _sceneHasWind;

    // Last frame's camera view/projection — lets Render() tell whether THIS frame's cache miss
    // is purely light-driven (camera unchanged) before allowing ShadowCache.CanReuseStaleFit to
    // substitute a stale fit; a moved camera always forces an immediate, exact redraw instead, so
    // camera motion never gets any extra latency from the light throttle.
    private Matrix4x4? _lastCameraView;
    private Matrix4x4? _lastCameraProj;

    private readonly ShadowCache _cache = new();

    public bool Active { get; private set; }
    public uint NearDepthTexture    => _mapsNear.DepthTexture;
    public uint NearRawDepthTexture => _mapsNear.RawDepthTexture;
    public uint FarDepthTexture     => _mapsFar.DepthTexture;
    public uint FarRawDepthTexture  => _mapsFar.RawDepthTexture;

    public Cascade[] Cascades { get; private set; } = [];

    // Physical resolution the given cascade index actually rendered at — see the tier fields'
    // comment. MainRenderer needs this (not the raw config Size) to compute each cascade's
    // world-space texel size correctly for PCSS's penumbra-to-texel conversion.
    public float Resolution(int cascade) => cascade == 0 ? _mapsNear.Size : _mapsFar.Size;

    public ShadowMapper(GL gl, AppConfig config, InstanceBuffer instances, Profiling.GPUProfiler profiler)
    {
        _gl = gl;
        _config = config;
        _instances = instances;
        _profiler = profiler;

        _cascadeBuilder = new CascadeBuilder(config);
        _mapsNear = new ShadowArray(gl, config.Shadows.Size, 1);
        _mapsFar  = new ShadowArray(gl, FarSize(config.Shadows), config.Shadows.MaxCascades - 1);
        _depth = new GLShader(gl,
            PathResolver.Resolve("Shaders/Shadow/depth.vert"),
            PathResolver.Resolve("Shaders/Shadow/depth.frag"));

        _solidByCascade    = new Dictionary<Model, List<InstanceData>>[config.Shadows.MaxCascades];
        _twoSidedByCascade = new Dictionary<Model, List<InstanceData>>[config.Shadows.MaxCascades];
        for (var c = 0; c < config.Shadows.MaxCascades; c++)
        {
            _solidByCascade[c]    = new Dictionary<Model, List<InstanceData>>();
            _twoSidedByCascade[c] = new Dictionary<Model, List<InstanceData>>();
        }
    }

    // Clamped so a very small scale (or scale=0) can't collapse the far tier to a degenerate
    // 0-size texture; never larger than the near tier's own size.
    private static uint FarSize(ShadowConfig s) =>
        (uint)Math.Clamp(MathF.Round(s.Size * s.FarCascadeScale), 64f, s.Size);

    public void Render(Scene scene, CullingSystem culling, ref FrameStats stats)
    {
        using var _ = Profiling.Tracy.Scope("ShadowMapper.Render");

        stats.ShadowCasters = 0;
        stats.ShadowCulled  = 0;

        Active = false;
        if (!_config.Shadows.Enabled) return;

        var wantFarSize = FarSize(_config.Shadows);
        if (_mapsNear.Size != _config.Shadows.Size)
        {
            _mapsNear.Dispose();
            _mapsNear = new ShadowArray(_gl, _config.Shadows.Size, 1);
            _cache.Invalidate();   // fresh, empty maps — must redraw
        }
        if (_mapsFar.Size != wantFarSize)
        {
            _mapsFar.Dispose();
            _mapsFar = new ShadowArray(_gl, wantFarSize, _config.Shadows.MaxCascades - 1);
            _cache.Invalidate();
        }

        if (scene.Lighting.DirectionalLights.Count == 0) return;

        var dir         = Vector3.Normalize(scene.Lighting.DirectionalLights[0].Direction);
        var camera      = scene.Cameras.Active;
        var sceneBounds = SceneBounds(scene);   // also refreshes _sceneHasWind (revision-gated)

        // Camera view/proj this frame, compared against last frame's below — a moved camera must
        // never be allowed the stale-fit reuse path (CanReuseStaleFit), only the light throttle.
        // Raw (unjittered) projection, same reasoning as GetFrustumCorners below: TAA's per-frame
        // jitter would otherwise make this comparison fail every frame regardless of whether the
        // camera actually moved, since it's baked into GetProjectionMatrix() but isn't a real
        // camera change.
        var view = camera.GetViewMatrix();
        var proj = camera.GetProjectionMatrixRaw();
        var cameraStatic = _lastCameraView == view && _lastCameraProj == proj;
        _lastCameraView = view;
        _lastCameraProj = proj;

        // Always refit — pure CPU math, no GL calls — so Cascades (read every frame by
        // MainRenderer.UploadShadowData for the lit shader's UBO) is exact for the current
        // camera even on a frame where the GPU redraw below ends up skipped. Only the
        // (expensive) redraw itself is gated by whether the fit actually changed — see
        // ShadowCache.
        //
        // resolutionOf tells the fit which physical resolution each cascade actually renders
        // at (cascade 0 -> near tier's Size, everything else -> the shared far tier), so its
        // texel-snap grid matches reality instead of always assuming cascade 0's resolution.
        Cascades = _cascadeBuilder.Build(camera, dir, sceneBounds,
            c => c == 0 ? _mapsNear.Size : _mapsFar.Size, Cascades);

        if (_cache.CanReuse(scene.Revision, Cascades,
                _sceneHasWind && _config.Foliage.WindAnimating,
                _config.Shadows.WindThrottleMs / 1000f))
        {
            Active = true;   // last frame's depth maps are still valid for this exact fit
            return;
        }

        // The fresh fit above didn't match what's rendered — normally that means an immediate
        // redraw. But if only the light rotated (camera and scene both unchanged) and we're still
        // inside the light throttle window, reuse anyway: substituting CachedCascades (not the
        // fresh fit just computed) keeps the sampling matrix consistent with the un-redrawn
        // texture — using the fresh fit here instead would sample last frame's depth content with
        // this frame's different light matrix, a real misalignment, not mere lag. See
        // ShadowCache.CanReuseStaleFit.
        if (_cache.CanReuseStaleFit(scene.Revision, cameraStatic, _config.Shadows.LightThrottleMs / 1000f))
        {
            // Clone, don't alias: next frame's Build() call mutates its `reuse` array (this
            // field) in place. Pointing Cascades straight at ShadowCache's own array would let
            // that in-place mutation silently corrupt the cache's stored record the moment this
            // reused fit gets refit again, without ever going through Record().
            Cascades = (Cascade[])_cache.CachedCascades.Clone();
            Active = true;
            return;
        }

        SetRenderState();
        _depth.Use();

        _depth.SetUniform("uAlbedo", 0);
        _depth.SetUniform("uFoliageAlphaCutoff", _config.Foliage.AlphaCutoff);
        ShaderUniformBinder.UploadWind(_depth, _config.Foliage);
        
        // Pass 1: cull + bucket every cascade up front. Pure CPU work (no GL state/draw calls),
        // so doing it ahead of any drawing lets the two categories below run as two
        // GPU-contiguous blocks instead of interleaving per cascade.
        var totalCasters = culling.EntityCount;
        using (Profiling.Tracy.Scope("ShadowMapper.Cull"))
        {
            for (var c = 0; c < Cascades.Length; c++)
            {
                _cull.Update(Cascades[c].Matrix);

                _cascadeVisible.Clear();
                culling.CullInto(_cull, _cascadeVisible);
                stats.ShadowCulled += totalCasters - _cascadeVisible.Count;

                BucketCasters(_cascadeVisible, _solidByCascade[c], _twoSidedByCascade[c]);
            }
        }

        // Pass 2: opaque casters, all cascades back-to-back — isolates their GPU cost from
        // foliage's in the profiler (see "ShadowsSolid"/"ShadowsFoliage" below). Depth-test
        // ordering doesn't care whether solid or foliage writes a cascade's layer first, so
        // this reorder (cascade-major -> category-major) doesn't change the rendered result.
        using (_profiler.Measure("ShadowsSolid"))
        using (Profiling.Tracy.Scope("ShadowMapper.Solid"))
        {
            SetSolidRenderState();
            for (var c = 0; c < Cascades.Length; c++)
            {
                var (tier, layer) = TierFor(c);
                tier.BindLayer(layer, clear: true);
                _depth.SetUniform("uLightMatrix", Cascades[c].Matrix);
                DrawGroups(_solidByCascade[c], ref stats);
            }
            ResetSolidRenderState();
        }

        // Pass 3: alpha-tested foliage casters, same idea. clear:false — the solid pass above
        // already cleared and wrote into this same cascade layer this frame.
        using (_profiler.Measure("ShadowsFoliage"))
        using (Profiling.Tracy.Scope("ShadowMapper.Foliage"))
        {
            for (var c = 0; c < Cascades.Length; c++)
            {
                var (tier, layer) = TierFor(c);
                tier.BindLayer(layer, clear: false);
                _depth.SetUniform("uLightMatrix", Cascades[c].Matrix);
                DrawGroups(_twoSidedByCascade[c], ref stats);
            }
        }

        ResetRenderState();

        _mapsNear.SyncRawDepth();
        if (Cascades.Length > 1)
            _mapsFar.SyncRawDepth();
        Active = true;

        _cache.Record(scene.Revision, Cascades);   // valid until the fit or scene changes
    }

    // Cascade 0 -> the near tier's single layer; every other cascade -> the far tier, at
    // index (c - 1).
    private (ShadowArray tier, int layer) TierFor(int c) =>
        c == 0 ? (_mapsNear, 0) : (_mapsFar, c - 1);

    private void BucketCasters(HashSet<Entity> visible,
        Dictionary<Model, List<InstanceData>> solid, Dictionary<Model, List<InstanceData>> twoSided)
    {
        foreach (var list in solid.Values)
            list.Clear();
        foreach (var list in twoSided.Values)
            list.Clear();

        foreach (var entity in visible)
        {
            if (entity.Model is not { } model) continue;

            var groups = entity.AnyTwoSided ? twoSided : solid;
            if (!groups.TryGetValue(model, out var list))
                groups[model] = list = new List<InstanceData>();

            _materials[model] = entity.Materials;
            list.Add(new InstanceData(entity.Transform.WorldMatrix));
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
        _mapsNear.Dispose();
        _mapsFar.Dispose();
        _depth.Dispose();
    }
}