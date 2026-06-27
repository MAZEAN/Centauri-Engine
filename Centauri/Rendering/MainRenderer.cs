namespace Centauri.Rendering;

using Silk.NET.OpenGL;
using System.Numerics;

using Config;
using World;
using Graphics.Resources;
using Graphics.Geometry;
using Graphics.Resources.Buffers;
using Culling;
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
    private readonly ShadowBuffer _shadowBuffer;
    private readonly HashSet<GLShader> _lightBlockBound = new();

    private readonly TextureBinder _textures;
    private readonly ShaderBatcher _batcher = new();
    private readonly ShaderUniformBinder _uniforms;
    
    private readonly InstanceBuffer _instanceBuffer;
    private readonly List<InstanceData> _instances = new();

    private GLShader? _activeShader;
    private CullingSystem _culling = null!;
    private Camera    _viewCamera = null!;
    private Matrix4x4 _view;
    private Vector3   _cameraPosition;
    
    private float     _iblScale;
    
    // Flags
    private bool _iblActive;
    private bool      _ssaoActive;
    private bool?     _twoSided;

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

    public void Render(Scene scene, float deltaTime, ref FrameStats stats, uint ssaoTexture, bool ssaoActive, CullingSystem culling)    {
        ResetFrameStats(ref stats);
        _textures.Reset();
        
        _activeShader = null;
        _twoSided     = null;
        _culling      = culling;
        
        if (ssaoActive)
        {
            _gl.ActiveTexture(TextureUnit.Texture9);
            _gl.BindTexture(TextureTarget.Texture2D, ssaoTexture);
        }

        _viewCamera = scene.Cameras.Active;

        _view           = _viewCamera.GetViewMatrix();
        _cameraPosition = _viewCamera.Position;
        _iblScale       = DaylightIblScale(scene);
        _ssaoActive     = ssaoActive;

        UploadLights(scene.Lighting);

        BindIbl(scene);
        BindShadows();
        UploadShadowData();

        var batches = _batcher.GetBatches(scene);

        stats.RenderableEntities = _batcher.RenderableEntities;
        stats.TwoSidedEntities   = _batcher.TwoSidedEntities;

        foreach (var batch in batches)
            DrawBatch(batch, ref stats);
        
        ResetSurfaceRenderState();
    }

    private void DrawBatch(Batch batch, ref FrameStats stats)
    {
        _instances.Clear();

        foreach (var entity in batch.Entities)
        {
            if (!entity.Enabled) continue;
            
            if (!_culling.IsVisible(entity))            {
                stats.CulledEntities++;
                continue;
            }
            _instances.Add(new InstanceData(entity.Transform.WorldMatrix, entity.UvScale, entity.UvOffset));
        }
        
        if (_instances.Count == 0) return;
        
        _instanceBuffer.Upload(_instances);

        stats.DrawnEntities += _instances.Count;
        stats.Batches++;

        var meshes = batch.Model.Meshes;
        for (var i = 0; i < meshes.Count; i++)
        {
            if (i >= batch.Materials.Length || batch.Materials[i] is not { } material)
                continue;

            var shader = EnsureShader(material.Shader);
            SetSurfaceRenderState(material.TwoSided);

            stats.TextureBinds += _textures.BindMaterial(material);
            ShaderUniformBinder.UploadMaterial(shader, material);

            var mesh = meshes[i];
            mesh.ConfigureInstancing(_instanceBuffer.Handle);
            mesh.DrawInstanced(_instances.Count);

            stats.DrawCalls      += 1;
            stats.NaiveDrawCalls += _instances.Count;
            stats.TotalIndices   += (int)mesh.IndexCount  * _instances.Count;
            stats.TotalVertices  += (int)mesh.VertexCount * _instances.Count;
        }
    }
    
    private void SetSurfaceRenderState(bool twoSided)
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

    
    private GLShader EnsureShader(GLShader shader)
    {
        if (ReferenceEquals(shader, _activeShader)) return shader;

        shader.Use();
        if (_lightBlockBound.Add(shader))
        {
            shader.BindUniformBlock("Lights",  LightBuffer.BindingPoint);
            shader.BindUniformBlock("Shadows", ShadowBuffer.BindingPoint);
        }

        shader.SetUniform("uView",      _view);
        shader.SetUniform("uCameraPos", _cameraPosition);
        _uniforms.UploadGlobals(shader, _viewCamera, _iblActive, _iblScale, _ssaoActive);

        _activeShader = shader;
        return shader;
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
    
    private void UploadShadowData()
    {
        if (!_shadows.Active) return;

        var cascades = _shadows.Cascades;
        float size   = _config.Shadows.Size;
        for (var i = 0; i < cascades.Length; i++)
            _shadowBuffer.SetCascade(i, cascades[i].Matrix, cascades[i].SplitDepth,
                cascades[i].Radius * 2f / size);

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
    }
}