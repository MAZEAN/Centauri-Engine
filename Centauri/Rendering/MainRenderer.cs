namespace Centauri.Rendering;

using Silk.NET.OpenGL;
using System.Numerics;

using Config;
using World;
using Graphics.Resources;
using Graphics.Geometry;
using World.Collections;
using World.Components;
using Utils.Misc;
using IBL;
using Shadows;

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

    private uint[] _boundTextures = null!;

    // shader-group batching cache — rebuilt when the scene's entity set changes  (#2)
    private readonly Dictionary<GLShader, List<Entity>> _shaderGroups = new();
    private int _groupsRevision = -1;

    // all lights live in one std140 UBO shared by every lit shader  (#3)
    private readonly LightBuffer _lightBuffer;
    private readonly HashSet<GLShader> _lightBlockBound = new();
    
    private bool _iblActive;
    private float _iblIntensityScale = 1f; 

    public MainRenderer(GL gl, AppConfig config, IBLBaker ibl, ShadowMapper shadows)
    {
        _gl = gl;
        _config = config;
        _ibl = ibl;
        _shadows = shadows;

        _lightBuffer = new LightBuffer(gl);
        InitializeTextureCache();
    }

    public void Render(Scene scene, float deltaTime, ref FrameStats stats)
    {
        ResetFrameStats(ref stats);
        Array.Fill(_boundTextures, uint.MaxValue);
        
        var viewCamera    = scene.Cameras.Active;
        var cullingCamera = scene.Cameras.Primary;
        
        cullingCamera.UpdateFrustum();

        var view          = viewCamera.GetViewMatrix();
        var cameraPosition = viewCamera.Position;

        UploadLights(scene.Lighting);
        
        _iblIntensityScale = DaylightIblScale(scene);
        
        BindIbl(scene);
        BindShadows();

        foreach (var (shader, entities) in GetGroups(scene))
        {
            shader.Use();
            
            if (_lightBlockBound.Add(shader))
                shader.BindUniformBlock("Lights", LightBuffer.BindingPoint);

            shader.SetUniform("uView",      view);
            shader.SetUniform("uCameraPos", cameraPosition);

            UploadGlobalUniforms(shader, viewCamera);

            foreach (var entity in entities)
            {
                if (!entity.Enabled) continue;
                if (entity.Model is not { } model || entity.Material is not { } mat) continue;

                if (_config.Debug.EnableCulling && !cullingCamera.Frustum.IsVisibleAABB(entity.GetWorldBounds()))
                {
                    stats.CulledEntities++; 
                    continue;
                }
                
                stats.TextureBinds += BindMaterialTextures(mat);
                stats.DrawnEntities++;

                DrawEntity(entity, shader, mat, model, ref stats);
            }
        }
    }

    private void DrawEntity(Entity entity, GLShader shader, Material mat, Model model, ref FrameStats stats)
    {
        UploadMaterialFlags(shader, mat);
        UploadTransform(shader, entity);
        UploadMaterialProperties(shader, mat, entity);

        foreach (var mesh in model.Meshes)
        {
            mesh.Bind();
            unsafe
            {
                _gl.DrawElements(PrimitiveType.Triangles, mesh.IndexCount,
                    DrawElementsType.UnsignedInt, (void*)0);
            }
            stats.DrawCalls++;
            stats.TotalIndices += (int) mesh.IndexCount;
            stats.TotalVertices += (int) mesh.VertexCount;
        }
    }
    
    private IReadOnlyDictionary<GLShader, List<Entity>> GetGroups(Scene scene)
    {
        if (scene.Revision == _groupsRevision)
            return _shaderGroups;

        _shaderGroups.Clear();

        foreach (var entity in scene.Entities)
        {
            if (entity.Material is not { } material)   // light-only / mesh-less entities
                continue;

            if (!_shaderGroups.TryGetValue(material.Shader, out var list))
            {
                list = new List<Entity>();
                _shaderGroups[material.Shader] = list;
            }

            list.Add(entity);
        }

        // sort each group by material so texture binds are minimized
        foreach (var list in _shaderGroups.Values)
            list.Sort((a, b) => a.Material!.SortKey.CompareTo(b.Material!.SortKey));

        _groupsRevision = scene.Revision;
        return _shaderGroups;
    }
    
    // -----------------------------
    // Material + Texture handling
    // -----------------------------
    private int BindMaterialTextures(Material mat)
    {
        var binds = 0;
        binds += BindTexture(mat.Albedo,    TextureUnit.Texture0);
        binds += BindTexture(mat.Normal,    TextureUnit.Texture1);
        binds += BindTexture(mat.Roughness, TextureUnit.Texture2);
        binds += BindTexture(mat.Metallic,  TextureUnit.Texture3);
        binds += BindTexture(mat.AO,        TextureUnit.Texture4);
        return binds;
    }

    private int BindTexture(GLTexture? tex, TextureUnit slot)
    {
        var index = (int)slot - (int)TextureUnit.Texture0;
        var handle = tex?.Handle ?? 0;

        if (index < 0 || index >= _boundTextures.Length)
            throw new Exception($"Texture slot {slot} exceeds supported range.");

        if (_boundTextures[index] == handle)
            return 0; // cache hit — no GPU bind

        _gl.ActiveTexture(slot);
        _gl.BindTexture(TextureTarget.Texture2D, handle);
        _boundTextures[index] = handle;
        return 1;
    }
    
    // -----------------------------
    // Global uniforms
    // -----------------------------
    private void UploadGlobalUniforms(GLShader shader, Camera camera)
    {
        var projection = camera.GetProjectionMatrix();

        shader.SetUniform("uProjection", projection);

        // texture unit bindings
        shader.SetUniform("uAlbedoMap",    0);
        shader.SetUniform("uNormalMap",    1);
        shader.SetUniform("uRoughnessMap", 2);
        shader.SetUniform("uMetallicMap",  3);
        shader.SetUniform("uAOMap",        4);
        
        // IBL bindings
        shader.SetUniform("uIrradianceMap", 5);
        shader.SetUniform("uPrefilterMap",  6);
        shader.SetUniform("uBrdfLUT",       7);
        shader.SetUniform("uHasIBL", _iblActive ? 1 : 0);
        shader.SetUniform("uMaxReflectionLod", (float)_ibl.MaxReflectionLod);
        shader.SetUniform("uIblIntensity", _config.IBLConfig.IblIntensity * _iblIntensityScale);
        
        // CSM bindings
        shader.SetUniform("uShadowMap", 8);
        shader.SetUniform("uHasShadow", _shadows.Active ? 1 : 0);
        shader.SetUniform("uShowCascades", _config.Shadows.DebugCascades ? 1 : 0);
        
        if (_shadows.Active)
        {
            var cascades = _shadows.Cascades;
            shader.SetUniform("uCascadeCount", cascades.Length);
            
            for (var i = 0; i < cascades.Length; i++)
            {
                shader.SetUniform($"uLightMatrices[{i}]", cascades[i].Matrix);
                shader.SetUniform($"uCascadeSplits[{i}]", cascades[i].SplitDepth);
                shader.SetUniform($"uTexelWorld[{i}]", cascades[i].Radius * 2f / _config.Shadows.Size);
            }
            
            shader.SetUniform("uShadowBias", _config.Shadows.DepthBias);
            shader.SetUniform("uNormalBias", _config.Shadows.NormalBias);
            shader.SetUniform("uPcfRadius",  _config.Shadows.PcfRadius);
        }
    }
    
    // -----------------------------
    // Material
    // -----------------------------
    private static void UploadMaterialFlags(GLShader shader, Material mat)
    {
        shader.SetUniform("uHasAlbedo",    mat.Albedo    != null ? 1 : 0);
        shader.SetUniform("uHasNormal",    mat.Normal    != null ? 1 : 0);
        shader.SetUniform("uHasRoughness", mat.Roughness != null ? 1 : 0);
        shader.SetUniform("uHasMetallic",  mat.Metallic  != null ? 1 : 0);
    }

    private static void UploadMaterialProperties(GLShader shader, Material mat, Entity entity)
    {
        shader.SetUniform("uRoughnessValue", mat.RoughnessValue);
        shader.SetUniform("uMetallicValue",  mat.MetallicValue);
        shader.SetUniform("uColor",          mat.Color);
        shader.SetUniform("uUvScale",        entity.UvScale);
        shader.SetUniform("uUvOffset",       entity.UvOffset);
    }
    
    // -----------------------------
    // Transform
    // -----------------------------
    private static void UploadTransform(GLShader shader, Entity entity)
    {
        var model = entity.Transform.WorldMatrix;

        shader.SetUniform("uModel", model);

        if (Matrix4x4.Invert(model, out var invModel))
            shader.SetUniformMat3X3("uNormalMatrix", Matrix4x4.Transpose(invModel));
        else
            shader.SetUniformMat3X3("uNormalMatrix", Matrix4x4.Transpose(model));
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

    private void InitializeTextureCache()
    {
        _gl.GetInteger(GLEnum.MaxTextureImageUnits, out var maxUnits);

        _boundTextures = new uint[maxUnits];
        Array.Fill(_boundTextures, uint.MaxValue);
    }
    
    private static void ResetFrameStats(ref FrameStats stats)
    {
        stats.DrawnEntities  = 0;
        stats.CulledEntities = 0;
        stats.DrawCalls      = 0;
        stats.TextureBinds   = 0;
        stats.TotalIndices   = 0;
        stats.TotalVertices  = 0;
    }
    
    public void Dispose() => _lightBuffer.Dispose();
}