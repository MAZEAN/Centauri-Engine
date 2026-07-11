namespace Centauri.Rendering.Prepass;

using Silk.NET.OpenGL;

using World;
using Graphics.Resources;
using Graphics.Resources.Materials;
using Graphics.Geometry;
using Utils.Misc;
using Config;
using Helper;
using Culling;

// Depth-only pass for the main forward view, drawn directly into the already-bound HDR
// framebuffer's own depth attachment before MainRenderer's colored draw, purely so the
// expensive PBR shader gets hardware early-Z rejection against overlapping/occluded geometry
// instead of shading every fragment regardless of final visibility. The biggest win is where
// overdraw is worst: alpha-tested, two-sided foliage viewed up close, where many leaf layers
// overlap on screen and Forward previously paid full shading cost for every one of them.
//
// Only actually runs when RenderingSystem couldn't instead borrow GeometryPrepass's own depth
// for this purpose (see HDRFramebuffer.TryBorrowDepth) — i.e. when GTAO/SSR/TAA are all off
// this frame (so Prepass didn't run), or the HDR target is genuinely multisampled. Either way,
// Forward's early-Z benefit is the same; this is just the fallback source for the depth it
// reuses.
public sealed class ZPrepass : IDisposable
{
    private readonly GL _gl;
    private readonly AppConfig _config;
    private readonly InstanceBuffer _instances;

    private readonly GLShader _shader;
    private readonly ShaderBatcher _batcher = new();
    private readonly List<InstanceData> _instanceData = [];

    private bool? _cullEnabled;

    public ZPrepass(GL gl, AppConfig config, InstanceBuffer instances)
    {
        _gl = gl;
        _config = config;
        _instances = instances;

        _shader = new GLShader(gl,
            PathResolver.Resolve("Shaders/Depth/zprepass.vert"),
            PathResolver.Resolve("Shaders/Depth/zprepass.frag"));
    }

    // Draws into whatever's currently bound (the HDR framebuffer) with color writes masked —
    // MainRenderer.Render must follow with DepthFunc(Lequal)/DepthMask(false) to actually
    // benefit from the depth this writes, then restore Less/true afterward.
    public void Render(Scene scene, Camera camera, CullingSystem culling)
    {
        BeginPass(camera);

        foreach (var batch in _batcher.GetBatches(scene))
            RenderBatch(batch, culling);

        EndPass();
    }

    private void BeginPass(Camera camera)
    {
        _gl.ColorMask(false, false, false, false);
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Less);
        _gl.DepthMask(true);
        _cullEnabled = null;
        SetCullState(true);

        _shader.Use();
        _shader.SetUniform("uView",                camera.GetViewMatrix());
        _shader.SetUniform("uProjection",          camera.GetProjectionMatrix());
        _shader.SetUniform("uAlbedo",              0);
        _shader.SetUniform("uFoliageAlphaCutoff",  _config.Foliage.AlphaCutoff);
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

    private void SetMeshState(Material? material)
    {
        _shader.SetUniform("uWind", material is { Wind: true } ? 1 : 0);

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

    private void SetCullState(bool enabled)
    {
        if (_cullEnabled == enabled) return;
        _cullEnabled = enabled;

        if (enabled) 
            _gl.Enable(EnableCap.CullFace);
        else         
            _gl.Disable(EnableCap.CullFace);
    }

    private void EndPass()
    {
        _gl.ColorMask(true, true, true, true);
    }

    public void Dispose() => _shader.Dispose();
}
