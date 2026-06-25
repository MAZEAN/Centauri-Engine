namespace Centauri.Rendering;

using Silk.NET.OpenGL;

using Config;
using World;
using Graphics.Resources;
using Graphics.Geometry;
using Graphics.Resources.Buffers;
using Graphics.Resources.Materials;
using World.Collections;
using World.Components;
using Utils.Misc;
using IBL;
using Shadows;
using Helper;

public class MainRenderer : IDisposable
{
    private readonly GL _gl;
    private readonly AppConfig _config;
    private readonly IBLBaker _ibl;
    private readonly ShadowMapper _shadows;

    private const float SpotConstant  = 1.0f;
    private const float SpotLinear    = 0.09f;
    private const float SpotQuadratic = 0.032f;

    private const float NightAmbient = 0.15f;   // moonlit IBL floor so night isn't pitch black

    private readonly LightBuffer _lightBuffer;
    private readonly HashSet<GLShader> _lightBlockBound = new();

    private readonly TextureBinder _textures;
    private readonly ShaderBatcher _batcher = new();
    private readonly ShaderUniformBinder _uniforms;
    
    private readonly InstanceBuffer _instanceBuffer;
    private readonly List<InstanceData> _instances = new();

    private bool _iblActive;

    public MainRenderer(GL gl, AppConfig config, IBLBaker ibl, ShadowMapper shadows)
    {
        _gl = gl;
        _config = config;
        _ibl = ibl;
        _shadows = shadows;

        _lightBuffer = new LightBuffer(gl);
        _textures    = new TextureBinder(gl);
        _uniforms    = new ShaderUniformBinder(config, ibl, shadows);
        _instanceBuffer = new InstanceBuffer(gl);
    }

    public void Render(Scene scene, float deltaTime, ref FrameStats stats, uint ssaoTexture, bool ssaoActive)
    {
        ResetFrameStats(ref stats);
        _textures.Reset();
        
        if (ssaoActive)
        {
            _gl.ActiveTexture(TextureUnit.Texture9);
            _gl.BindTexture(TextureTarget.Texture2D, ssaoTexture);
        }

        var viewCamera    = scene.Cameras.Active;
        var cullingCamera = scene.Cameras.Primary;

        cullingCamera.UpdateFrustum();

        var view           = viewCamera.GetViewMatrix();
        var cameraPosition = viewCamera.Position;

        UploadLights(scene.Lighting);

        var iblScale = DaylightIblScale(scene);

        BindIbl(scene);
        BindShadows();

        var cull = _config.Debug.EnableCulling;
        foreach (var (shader, batches) in _batcher.GetGroups(scene))
        {
            shader.Use();

            if (_lightBlockBound.Add(shader))
                shader.BindUniformBlock("Lights", LightBuffer.BindingPoint);

            shader.SetUniform("uView",      view);
            shader.SetUniform("uCameraPos", cameraPosition);

            _uniforms.UploadGlobals(shader, viewCamera, _iblActive, iblScale, ssaoActive);

            foreach (var batch in batches)
                DrawBatch(batch, shader, cullingCamera, cull, ref stats);
        }
    }

    private void DrawBatch(Batch batch, GLShader shader, Camera cullCamera, bool cull, ref FrameStats stats)
    {
        _instances.Clear();

        foreach (var entity in batch.Entities)
        {
            if (!entity.Enabled) continue;
            
            if (cull && !cullCamera.Frustum.IsVisibleAABB(entity.GetWorldBounds()))
            {
                stats.CulledEntities++;
                continue;
            }
            _instances.Add(new InstanceData(entity.Transform.WorldMatrix, entity.UvScale, entity.UvOffset));
        }
        
        if (_instances.Count == 0) return;

        stats.TextureBinds += _textures.BindMaterial(batch.Material);
        ShaderUniformBinder.UploadMaterial(shader, batch.Material);
        _instanceBuffer.Upload(_instances);

        stats.DrawnEntities += _instances.Count;
        stats.Batches++;

        foreach (var mesh in batch.Model.Meshes)
        {
            mesh.ConfigureInstancing(_instanceBuffer.Handle);
            mesh.DrawInstanced(_instances.Count);

            stats.DrawCalls     += 1;
            stats.TotalIndices  += (int)mesh.IndexCount  * _instances.Count;
            stats.TotalVertices += (int)mesh.VertexCount * _instances.Count;
        }
    }

    // -----------------------------
    // Lighting
    // -----------------------------
    private void UploadLights(LightingSystem lights)
    {
        _lightBuffer.Begin();

        if (lights.DirectionalLights.Count > 0)
        {
            var d = lights.DirectionalLights[0];
            _lightBuffer.SetDirectional(d.Direction, d.Color, d.Intensity);
        }

        foreach (var p in lights.PointLights)
            _lightBuffer.AddPoint(
                p.Position, p.Light.Color, p.Light.Intensity,
                p.Light.Constant, p.Light.Linear, p.Light.Quadratic);

        foreach (var s in lights.SpotLights)
            _lightBuffer.AddSpot(
                s.Position, s.Light.Direction, s.Light.Color, s.Light.Intensity,
                SpotConstant, SpotLinear, SpotQuadratic,
                s.Light.InnerCutoff, s.Light.OuterCutoff);

        _lightBuffer.Upload();
    }

    // dim ambient/IBL toward a moonlit floor when a day/night cycle drives the scene
    private static float DaylightIblScale(Scene scene) =>
        scene.FindComponent<DayNightCycle>() is { } cycle
            ? NightAmbient + (1f - NightAmbient) * cycle.Daylight
            : 1f;

    private void BindIbl(Scene scene)
    {
        _iblActive = scene.Skyboxes.Active is { IblBaked: true };
        if (!_iblActive) return;

        var sky = scene.Skyboxes.Active!;
        _gl.ActiveTexture(TextureUnit.Texture5);
        _gl.BindTexture(TextureTarget.TextureCubeMap, sky.IrradianceMap);
        _gl.ActiveTexture(TextureUnit.Texture6);
        _gl.BindTexture(TextureTarget.TextureCubeMap, sky.PrefilteredMap);
        _gl.ActiveTexture(TextureUnit.Texture7);
        _gl.BindTexture(TextureTarget.Texture2D, _ibl.BrdfLut);
    }

    private void BindShadows()
    {
        if (!_shadows.Active) return;
        _gl.ActiveTexture(TextureUnit.Texture8);
        _gl.BindTexture(TextureTarget.Texture2DArray, _shadows.DepthTexture);
    }

    private static void ResetFrameStats(ref FrameStats stats)
    {
        stats.DrawnEntities  = 0;
        stats.CulledEntities = 0;
        stats.DrawCalls      = 0;
        stats.TextureBinds   = 0;
        stats.TotalIndices   = 0;
        stats.TotalVertices  = 0;
        stats.Batches        = 0;
    }

    public void Dispose()
    {
        _lightBuffer.Dispose();
        _instanceBuffer.Dispose();
    }
}