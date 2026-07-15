namespace Centauri.Rendering.Shadows;

using Silk.NET.OpenGL;
using System.Numerics;

using Config;
using World;
using World.Collections;
using Utils.Misc;
using Graphics.Resources;
using Graphics.Resources.Materials;
using Graphics.Geometry;
using Utils.Geometry;
using Helper;
using Culling;

// Shadow maps for spot lights opted in via SpotLight.CastsShadow — see
// Docs/Documentation/LocalShadows.md for why this exists as a separate, simpler pass rather than
// folding into ShadowMapper (CSM's texel-snapped orthographic fit doesn't apply to a perspective
// local-light frustum) and why point lights aren't covered (GL 3.3 core has no cubemap array —
// ARB_texture_cube_map_array is GL 4.0+ — so batching several point-light cubemaps the way this
// batches spot frustums into one Texture2DArray isn't available; deferred, not attempted here).
//
// One shared ShadowArray atlas (SpotShadowConfig.MaxShadowSpots layers) — the same GL resource
// ShadowMapper uses per cascade, just parameterized per-light instead of per-cascade. No PCSS
// contact-hardening in this pass (unlike CSM): a fixed PCF radius keeps the per-light cost and
// the config surface small; ShadowArray's "raw" uncompared copy it'd need is therefore never
// synced/bound here, only ever the compare-mode texture.
public sealed class SpotShadowMapper : IDisposable
{
    private const float SlopeBias    = 3.0f;   // spot frustums are steeper than CSM's ortho slices
    private const float ConstantBias = 6.0f;
    private const float NearPlane    = 0.05f;

    private readonly GL _gl;
    private readonly AppConfig _config;
    private readonly InstanceBuffer _instances;
    private readonly Profiling.GPUProfiler _profiler;

    private ShadowArray _atlas;
    private readonly GLShader _depth;
    private readonly Frustum _cull = new();

    private readonly Dictionary<Model, List<InstanceData>>[] _solidBySlot;
    private readonly Dictionary<Model, List<InstanceData>>[] _twoSidedBySlot;
    private readonly Dictionary<Model, IReadOnlyList<Material?>> _materials = new();
    private readonly HashSet<Entity> _visible = new();

    // Stable slot assignment: a light keeps the same atlas layer frame-to-frame for as long as
    // it stays selected, so an unrelated light losing/gaining a slot elsewhere in the scene never
    // forces a redraw of this one. Cleared/rebuilt each frame in SelectActive.
    private readonly Dictionary<SpotLight, int> _slotOf = new();
    private readonly Matrix4x4[] _slotMatrix;

    // Per-slot redraw cache: cheap identity/value comparison, not CSM's texel-snap machinery — a
    // perspective local-light frustum has no analogous "stable fit" to reuse across small camera
    // moves, so the only thing worth skipping is a slot whose light truly didn't change and whose
    // scene revision hasn't moved (a conservative proxy for "no caster in range moved either").
    private readonly record struct SlotSnapshot(int Revision, Vector3 Position, Vector3 Direction,
        float InnerCutoff, float OuterCutoff, float Range);
    private readonly SlotSnapshot?[] _slotSnapshot;

    public bool Active { get; private set; }
    public uint AtlasDepthTexture => _atlas.DepthTexture;
    public int  ActiveSlots { get; private set; }

    public SpotShadowMapper(GL gl, AppConfig config, InstanceBuffer instances, Profiling.GPUProfiler profiler)
    {
        _gl = gl;
        _config = config;
        _instances = instances;
        _profiler = profiler;

        var maxSlots = config.SpotShadows.MaxShadowSpots;
        _atlas = new ShadowArray(gl, config.SpotShadows.Size, maxSlots);
        _depth = new GLShader(gl,
            PathResolver.Resolve("Shaders/Shadow/depth.vert"),
            PathResolver.Resolve("Shaders/Shadow/depth.frag"));

        _solidBySlot    = new Dictionary<Model, List<InstanceData>>[maxSlots];
        _twoSidedBySlot = new Dictionary<Model, List<InstanceData>>[maxSlots];
        for (var i = 0; i < maxSlots; i++)
        {
            _solidBySlot[i]    = new Dictionary<Model, List<InstanceData>>();
            _twoSidedBySlot[i] = new Dictionary<Model, List<InstanceData>>();
        }

        _slotMatrix   = new Matrix4x4[maxSlots];
        _slotSnapshot = new SlotSnapshot?[maxSlots];
    }

    // -1 if this light has no shadow slot this frame (not selected, or spot shadows disabled).
    public int SlotOf(SpotLight light) => Active && _slotOf.TryGetValue(light, out var slot) ? slot : -1;

    public Matrix4x4 SlotMatrix(int slot) => _slotMatrix[slot];

    public void Render(Scene scene, CullingSystem culling, Camera camera, ref FrameStats stats)
    {
        using var _ = Profiling.Tracy.Scope("SpotShadowMapper.Render");

        Active = false;
        ActiveSlots = 0;

        var maxSlots = _config.SpotShadows.MaxShadowSpots;
        if (!_config.SpotShadows.Enabled)
        {
            _slotOf.Clear();
            return;
        }

        if (_atlas.Size != _config.SpotShadows.Size)
        {
            _atlas.Dispose();
            _atlas = new ShadowArray(_gl, _config.SpotShadows.Size, maxSlots);
            Array.Clear(_slotSnapshot);   // fresh, empty atlas — every slot must redraw
        }

        var selected = SelectActive(scene.Lighting.SpotLights, camera.Position, maxSlots);
        if (selected.Count == 0)
        {
            _slotOf.Clear();
            return;
        }

        AssignSlots(selected);

        SetRenderState();
        _depth.Use();
        _depth.SetUniform("uAlbedo", 0);
        _depth.SetUniform("uFoliageAlphaCutoff", _config.Foliage.AlphaCutoff);
        ShaderUniformBinder.UploadWind(_depth, _config.Foliage);

        var totalCasters = culling.EntityCount;

        // One profiler zone around every slot this frame actually redraws — opened unconditionally
        // (like ShadowMapper's own zones) rather than only when redrewAny, so a frame that redraws
        // nothing still reports a real (near-zero) GPU time instead of vanishing from the graph.
        using (_profiler.Measure("SpotShadows"))
        using (Profiling.Tracy.Scope("SpotShadowMapper.Draw"))
        {
            foreach (var s in selected)
            {
                var light    = s.Light;
                var position = s.Position;
                var slot = _slotOf[light];
                var matrix = BuildMatrix(light, position);
                _slotMatrix[slot] = matrix;

                var snapshot = new SlotSnapshot(scene.Revision, position,
                    Vector3.Normalize(light.Direction), light.InnerCutoff, light.OuterCutoff, light.Range);

                if (_slotSnapshot[slot] == snapshot)
                    continue;   // this slot's light + scene are exactly as they were — atlas layer still valid

                _slotSnapshot[slot] = snapshot;

                _cull.Update(matrix);
                _visible.Clear();
                culling.CullInto(_cull, _visible);
                stats.ShadowCulled += totalCasters - _visible.Count;

                BucketCasters(_visible, _solidBySlot[slot], _twoSidedBySlot[slot]);

                _atlas.BindLayer(slot, clear: true);
                _depth.SetUniform("uLightMatrix", matrix);

                SetSolidRenderState();
                DrawGroups(_solidBySlot[slot], ref stats);
                ResetSolidRenderState();

                DrawGroups(_twoSidedBySlot[slot], ref stats);
            }
        }

        ResetRenderState();

        Active = true;
        ActiveSlots = selected.Count;
    }

    // Nearest-to-camera first: with more shadow-requesting lights than MaxShadowSpots, the ones
    // actually close enough to matter win over ones further away — mirrors LightingSystem's own
    // MAX_POINT_LIGHTS/MAX_SPOT_LIGHTS capping in spirit (graceful degradation, not a hard error).
    private static List<LightingSystem.ActiveSpot> SelectActive(
        IReadOnlyList<LightingSystem.ActiveSpot> spots, Vector3 cameraPos, int maxSlots)
    {
        var eligible = new List<LightingSystem.ActiveSpot>();
        foreach (var s in spots)
            if (s.Light.CastsShadow)
                eligible.Add(s);

        if (eligible.Count > maxSlots)
            eligible.Sort((a, b) =>
                Vector3.DistanceSquared(a.Position, cameraPos)
                    .CompareTo(Vector3.DistanceSquared(b.Position, cameraPos)));

        if (eligible.Count > maxSlots)
            eligible.RemoveRange(maxSlots, eligible.Count - maxSlots);

        return eligible;
    }

    // Keeps each already-selected light's existing layer (see the _slotOf field comment); only
    // newly-selected lights draw from the free-slot pool, and only losing a slot (not just
    // reordering relative to other lights) ever forces that light's old layer to be reused by
    // someone else.
    private void AssignSlots(List<LightingSystem.ActiveSpot> selected)
    {
        var maxSlots = _config.SpotShadows.MaxShadowSpots;
        var stillSelected = new HashSet<SpotLight>();
        foreach (var s in selected) stillSelected.Add(s.Light);

        foreach (var light in _slotOf.Keys.Where(l => !stillSelected.Contains(l)).ToList())
            _slotOf.Remove(light);

        var used = new bool[maxSlots];
        foreach (var slot in _slotOf.Values) used[slot] = true;

        foreach (var s in selected)
        {
            if (_slotOf.ContainsKey(s.Light)) continue;
            var free = Array.IndexOf(used, false);
            used[free] = true;
            _slotOf[s.Light] = free;
        }
    }

    private static Matrix4x4 BuildMatrix(SpotLight light, Vector3 position)
    {
        var dir = light.Direction.LengthSquared() > 1e-8f ? Vector3.Normalize(light.Direction) : -Vector3.UnitY;
        var up  = MathF.Abs(Vector3.Dot(dir, Vector3.UnitY)) > 0.99f ? Vector3.UnitX : Vector3.UnitY;

        var view = Matrix4x4.CreateLookAt(position, position + dir, up);
        // Outer cutoff is the half-angle; the frustum needs the full cone, clamped shy of 180°
        // so CreatePerspectiveFieldOfView never sees a degenerate/negative tangent.
        var fov  = Math.Clamp(light.OuterCutoff * 2f * (MathF.PI / 180f), 0.01f, MathF.PI - 0.01f);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(fov, 1f, NearPlane, MathF.Max(light.Range, NearPlane + 0.1f));

        return view * proj;
    }

    private void BucketCasters(HashSet<Entity> visible,
        Dictionary<Model, List<InstanceData>> solid, Dictionary<Model, List<InstanceData>> twoSided)
    {
        foreach (var list in solid.Values) list.Clear();
        foreach (var list in twoSided.Values) list.Clear();

        foreach (var entity in visible)
        {
            if (entity.Model is not { } model) continue;

            var groups = entity.AnyTwoSided ? twoSided : solid;
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

    private void SetRenderState() => _gl.Enable(EnableCap.CullFace);

    private void ResetRenderState()
    {
        _gl.Disable(EnableCap.PolygonOffsetFill);
        _gl.CullFace(TriangleFace.Back);
        _gl.Enable(EnableCap.CullFace);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public void Dispose()
    {
        _atlas.Dispose();
        _depth.Dispose();
    }
}
