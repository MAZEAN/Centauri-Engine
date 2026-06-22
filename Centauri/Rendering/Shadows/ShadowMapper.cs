namespace Centauri.Rendering.Shadows;

using Silk.NET.OpenGL;
using System.Numerics;

using Config;
using World;
using Utils.Misc;
using Graphics.Resources;
using Utils.Geometry;

public sealed class ShadowMapper : IDisposable
{
    private readonly GL _gl;
    private readonly AppConfig _config;
    private ShadowArray _maps;
    private readonly GLShader _depth;
    private readonly Frustum _cull = new();
    private readonly CascadeBuilder _cascadeBuilder;

    public bool Active { get; private set; }
    public uint DepthTexture => _maps.DepthTexture;

    public Cascade[] Cascades { get; private set; } = [];

    public ShadowMapper(GL gl, AppConfig config)
    {
        _gl = gl;
        _config = config;
        _cascadeBuilder = new CascadeBuilder(config);
        // pre-allocate every layer up front — cascade-count changes never re-alloc (no frame stall)
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
            _cull.Update(Cascades[c].Matrix);

            foreach (var entity in scene.Entities)
            {
                if (!entity.Enabled || entity.Model is not { } model)
                    continue;

                if (!_cull.IsVisibleAABB(entity.GetWorldBounds()))
                {
                    stats.ShadowCulled++;
                    continue;
                }       
                
                // solid casters record BACK-face depth (cull front) to avoid self-shadow
                // acne; two-sided casters have no back face, so draw both sides
                if (entity.Material is { TwoSided: true })
                    _gl.Disable(EnableCap.CullFace);
                else
                {
                    _gl.Enable(EnableCap.CullFace);
                    _gl.CullFace(TriangleFace.Front);
                }

                stats.ShadowCasters++;
                
                _depth.SetUniform("uModel", entity.Transform.WorldMatrix);
                foreach (var mesh in model.Meshes)
                {
                    mesh.Bind();
                    unsafe
                    {
                        _gl.DrawElements(PrimitiveType.Triangles, mesh.IndexCount,
                            DrawElementsType.UnsignedInt, (void*)0);
                    }
                }
            }
        }

        ResetRenderState();
        Active = true;
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