namespace Centauri.Rendering;

using Silk.NET.OpenGL;
using System.Numerics;

using Config;
using World;
using Graphics.Geometry;
using Graphics.Resources;
using Graphics.Resources.Buffers;
using Graphics.Resources.Materials;
using Culling;
using World.Collections;
using World.Components;
using Utils.Misc;
using IBL;
using Shadows;
using Helper;

public readonly record struct RenderRequest(
    Scene Scene,
    float DeltaTime,
    uint SsaoTexture,
    bool SsaoActive,
    CullingSystem Culling,
    Camera Camera,
    Matrix4x4 View,
    Vector3 Position,
    Vector4 ClipPlane = default
);

public class MainRenderer : IDisposable
{
    private readonly GL _gl;
    private readonly AppConfig _config;
    private readonly IBLBaker _ibl;
    private readonly ShadowMapper _shadows;

    private const float SpotConstant  = 1.0f;
    private const float SpotLinear    = 0.09f;
    private const float SpotQuadratic = 0.032f;
    private const float NightAmbient = 0.15f;

    private readonly LightBuffer _lightBuffer;
    private readonly ShadowBuffer _shadowBuffer;
    private readonly HashSet<GLShader> _lightBlockBound = [];

    private readonly TextureBinder _textures;
    private readonly ShaderBatcher _batcher = new();
    private readonly ShaderUniformBinder _uniforms;
    
    private readonly InstanceBuffer _instanceBuffer;
    private readonly List<InstanceData> _instances = [];
    
    private readonly record struct RenderContext(
        Camera Camera,
        Matrix4x4 View,
        Vector3 CameraPosition,
        float IblScale,
        bool SsaoActive,
        CullingSystem Culling,
        Vector4 ClipPlane 
    );

    private GLShader? _activeShader;
    
    // Flags
    private bool  _iblActive;
    private bool? _twoSided;

    public MainRenderer(GL gl, AppConfig config, IBLBaker ibl, ShadowMapper shadows, InstanceBuffer instances)
    {
        _gl = gl;
        _config = config;
        _ibl = ibl;
        _shadows = shadows;

        _lightBuffer = new LightBuffer(gl);
        _shadowBuffer = new ShadowBuffer(gl);
        _textures    = new TextureBinder(gl);
        _uniforms    = new ShaderUniformBinder(config, ibl, shadows);
        _instanceBuffer = instances;
    }

    public void Render(Scene scene, float deltaTime, ref FrameStats stats,
        uint ssaoTexture, bool ssaoActive, CullingSystem culling)
    {
        var camera = scene.Cameras.Active;
        Render(new RenderRequest(scene, deltaTime, ssaoTexture, ssaoActive, culling,
            camera, camera.GetViewMatrix(), camera.Position), ref stats);
    }

    public void Render(in RenderRequest request, ref FrameStats stats)
    {
        using var _ = Profiling.Tracy.Scope("MainRenderer.Render");

        var context = new RenderContext(
            request.Camera,
            request.View,
            request.Position,
            DaylightIblScale(request.Scene),
            request.SsaoActive,
            request.Culling,
            request.ClipPlane
        );
        BeginFrame(request.Scene, request.DeltaTime, ref stats, request.SsaoTexture, request.SsaoActive);

        var batches = _batcher.GetBatches(request.Scene);

        stats.RenderableEntities = _batcher.RenderableEntities;
        stats.TwoSidedEntities   = _batcher.TwoSidedEntities;

        foreach (var batch in batches)
            DrawBatch(batch, context, ref stats);

        ResetSurfaceRenderState();
    }

    private void BeginFrame(Scene scene, float deltaTime, ref FrameStats stats, uint ssaoTexture, bool ssaoActive)
    {
        _textures.Reset();
        ResetFrameStats(ref stats);

        _activeShader = null;
        _twoSided = null;
        
        BindSsao(ssaoTexture, ssaoActive);
        UploadLights(scene.Lighting);
        BindIbl(scene);
        BindShadows();
        UploadShadowData();
    }

    private void DrawBatch(Batch batch, RenderContext context, ref FrameStats stats)
    {
        var count = CollectVisibleInstances(batch, context, ref stats);
        if (count == 0)
            return;
        
        _instanceBuffer.Upload(_instances);

        stats.DrawnEntities += _instances.Count;
        stats.Batches++;

        var meshes = batch.Model.Meshes;
        for (var i = 0; i < meshes.Count; i++)
        {
            if (i >= batch.Materials.Length || batch.Materials[i] is not { } material)
                continue;

            DrawMesh(meshes[i], material, context, count, ref stats);
        }
    }
    
    private int CollectVisibleInstances(Batch batch, RenderContext context, ref FrameStats stats)
    {
        _instances.Clear();

        foreach (var entity in batch.Entities)
        {
            if (!entity.Enabled)
                continue;

            if (!context.Culling.IsVisible(entity))
            {
                stats.CulledEntities++;
                continue;
            }

            _instances.Add(
                new InstanceData(
                    entity.Transform.WorldMatrix,
                    entity.UvScale,
                    entity.UvOffset
                )
           );
        }

        return _instances.Count;
    }

    private void DrawMesh(Mesh mesh, Material? material, RenderContext context, int instanceCount, ref FrameStats stats)
    {
        if (material is null)
            return;

        var shader = BindShader(material.Shader, context);

        ApplySurfaceState(material.TwoSided);

        RegisterDraw(ref stats, mesh, material, instanceCount);

        ShaderUniformBinder.UploadMaterial(shader, material);

        mesh.ConfigureInstancing(_instanceBuffer.Handle);
        mesh.DrawInstanced(instanceCount);
        
    }
    
    private void RegisterDraw(ref FrameStats stats, Mesh mesh, Material? material, int instanceCount)
    {
        stats.TextureBinds += _textures.BindMaterial(material);
        stats.DrawCalls++;
        stats.NaiveDrawCalls += instanceCount;
        stats.TotalIndices += (int)mesh.IndexCount * instanceCount;
        stats.TotalVertices += (int)mesh.VertexCount * instanceCount;
    }
    
    private void ApplySurfaceState(bool twoSided)
    {
        if (_twoSided == twoSided) return;
        _twoSided = twoSided;

        if (twoSided)
        {
            _gl.Disable(EnableCap.CullFace);
            _gl.Enable(EnableCap.SampleAlphaToCoverage);
        }
        else
        {
            _gl.Enable(EnableCap.CullFace);
            _gl.Disable(EnableCap.SampleAlphaToCoverage);
        }
    }

    private void ResetSurfaceRenderState()
    {
        _gl.Enable(EnableCap.CullFace);
        _gl.Disable(EnableCap.SampleAlphaToCoverage);
    }
    
    private GLShader BindShader(GLShader shader, RenderContext context)
    {
        if (ReferenceEquals(shader, _activeShader)) return shader;

        shader.Use();
        if (_lightBlockBound.Add(shader))
        {
            shader.BindUniformBlock("Lights",  LightBuffer.BindingPoint);
            shader.BindUniformBlock("Shadows", ShadowBuffer.BindingPoint);
        }

        shader.SetUniform("uView",      context.View);
        shader.SetUniform("uCameraPos", context.CameraPosition);
        shader.SetUniform("uClipPlane", context.ClipPlane);
        _uniforms.UploadGlobals(shader, context.Camera, _iblActive, context.IblScale, context.SsaoActive);

        _activeShader = shader;
        return shader;
    }
    
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
    
    private static float DaylightIblScale(Scene scene) =>
        scene.FindComponent<DayNightCycle>() is { } cycle
            ? NightAmbient + (1f - NightAmbient) * cycle.Daylight
            : 1f;

    private void BindIbl(Scene scene)
    {
        var procedural = _config.Sky.Procedural && _ibl.HasProceduralBake && DayNightCycle.IsDay(scene);
        var sky        = scene.Skyboxes.Active;

        _iblActive = procedural || sky is { IblBaked: true };
        
        if (!_iblActive) return;

        _gl.ActiveTexture(TextureUnit.Texture5);
        _gl.BindTexture(TextureTarget.TextureCubeMap, procedural ? _ibl.ProceduralIrradiance : sky!.IrradianceMap);
        _gl.ActiveTexture(TextureUnit.Texture6);
        _gl.BindTexture(TextureTarget.TextureCubeMap, procedural ? _ibl.ProceduralPrefiltered : sky!.PrefilteredMap);
        _gl.ActiveTexture(TextureUnit.Texture7);
        _gl.BindTexture(TextureTarget.Texture2D, _ibl.BrdfLut);
    }
    
    private void BindSsao(uint texture, bool active)
    {
        if (active)
        {
            _gl.ActiveTexture(TextureUnit.Texture9);
            _gl.BindTexture(TextureTarget.Texture2D, texture);
        }
    }

    private void BindShadows()
    {
        if (!_shadows.Active) return;
        
        _gl.ActiveTexture(TextureUnit.Texture8);
        _gl.BindTexture(TextureTarget.Texture2DArray, _shadows.DepthTexture);
        _gl.ActiveTexture(TextureUnit.Texture10);
        _gl.BindTexture(TextureTarget.Texture2DArray, _shadows.RawDepthTexture);
    }
    
    private void UploadShadowData()
    {
        if (!_shadows.Active) return;

        var cascades = _shadows.Cascades;
        float size   = _config.Shadows.Size;
        for (var i = 0; i < cascades.Length; i++)
            _shadowBuffer.SetCascade(i, cascades[i].Matrix, cascades[i].SplitDepth,
                cascades[i].Radius * 2f / size, cascades[i].DepthRange);

        _shadowBuffer.Upload();
    }

    private static void ResetFrameStats(ref FrameStats stats)
    {
        stats.DrawnEntities  = 0;
        stats.CulledEntities = 0;
        stats.DrawCalls      = 0;
        stats.NaiveDrawCalls = 0;
        stats.TextureBinds   = 0;
        stats.TotalIndices   = 0;
        stats.TotalVertices  = 0;
        stats.Batches        = 0;
    }

    public void Dispose()
    {
        _lightBuffer.Dispose();
        _shadowBuffer.Dispose();
    }
}