namespace Centauri.Rendering.Shadows;

using Silk.NET.OpenGL;

using Config;
using World;
using Utils.Misc;
using Graphics.Resources;
using Graphics.Resources.Materials;
using Graphics.Geometry;
using Utils.Geometry;

// What ShadowMapper (CSM) and SpotShadowMapper share: both render depth-only casters from a
// light's point of view into a layer of a shared ShadowArray atlas, using the same depth shader,
// the same solid/two-sided-foliage caster split, and the same instanced draw path. What they
// don't share — CSM's texel-snapped orthographic cascade fit + ShadowCache reuse vs. a spot
// light's perspective frustum + stable-slot assignment — stays in each subclass; this base only
// owns the parts that were previously copy-pasted identically between them.
public abstract class ShadowCasterRenderer : IDisposable
{
    protected readonly GL Gl;
    protected readonly AppConfig Config;
    protected readonly Profiling.GPUProfiler Profiler;
    protected readonly GLShader Depth;
    protected readonly Frustum Cull = new();

    private readonly InstanceBuffer _instances;
    private readonly Dictionary<Model, IReadOnlyList<Material?>> _materials = new();

    protected ShadowCasterRenderer(GL gl, AppConfig config, InstanceBuffer instances, Profiling.GPUProfiler profiler)
    {
        Gl = gl;
        Config = config;
        _instances = instances;
        Profiler = profiler;

        Depth = new GLShader(gl,
            PathResolver.Resolve("Shaders/Shadow/depth.vert"),
            PathResolver.Resolve("Shaders/Shadow/depth.frag"));
    }

    protected void BucketCasters(HashSet<Entity> visible,
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

    protected void DrawGroups(Dictionary<Model, List<InstanceData>> groups, ref FrameStats stats)
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
        Depth.SetUniform("uWind", material is { Wind: true } ? 1 : 0);
        if (material is { TwoSided: true, Albedo: { } albedo })
        {
            Depth.SetUniform("uAlphaTest", 1);
            Gl.ActiveTexture(TextureUnit.Texture0);
            Gl.BindTexture(TextureTarget.Texture2D, albedo.Handle);
        }
        else
        {
            Depth.SetUniform("uAlphaTest", 0);
        }
    }

    // slopeBias/constantBias are per-subclass (spot frustums are steeper than CSM's ortho
    // slices and need a larger offset — see each subclass's own SlopeBias/ConstantBias consts).
    protected void SetSolidRenderState(float slopeBias, float constantBias)
    {
        Gl.Enable(EnableCap.CullFace);
        Gl.CullFace(TriangleFace.Front);
        Gl.Enable(EnableCap.PolygonOffsetFill);
        Gl.PolygonOffset(slopeBias, constantBias);
    }

    protected void ResetSolidRenderState()
    {
        Gl.Disable(EnableCap.PolygonOffsetFill);
        Gl.Disable(EnableCap.CullFace);
    }

    protected void SetRenderState() => Gl.Enable(EnableCap.CullFace);

    protected void ResetRenderState()
    {
        Gl.Disable(EnableCap.PolygonOffsetFill);
        Gl.CullFace(TriangleFace.Back);
        Gl.Enable(EnableCap.CullFace);
        Gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public virtual void Dispose() => Depth.Dispose();
}
