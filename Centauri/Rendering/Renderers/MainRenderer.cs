namespace Centauri.Rendering.Renderers;

using Silk.NET.OpenGL;
using System.Numerics;

using Config;
using World;
using Resources;
using Utils.Misc;
using Geometry;

public class MainRenderer : IDisposable
{
    private readonly GL _gl;
    private readonly AppConfig _config;
    
    private const int MaxPointLights = 16;
    private const int MaxSpotLights  = 16;

    private uint[] _boundTextures = null!;

    // shader-group batching cache — rebuilt when the scene's entity set changes  (#2)
    private readonly Dictionary<GLShader, List<Entity>> _shaderGroups = new();
    private int _groupsRevision = -1;

    // all lights live in one std140 UBO shared by every lit shader  (#3)
    private readonly LightBuffer _lightBuffer;
    private readonly HashSet<GLShader> _lightBlockBound = new();

    public MainRenderer(GL gl, AppConfig config)
    {
        _gl = gl;
        _config = config;

        _lightBuffer = new LightBuffer(gl);
        InitializeTextureCache();
    }

    public void Render(Scene scene, float deltaTime, ref FrameStats stats)
    {
        var viewCamera    = scene.Cameras.Active;
        var cullingCamera = scene.Cameras.Primary;
        
        cullingCamera.Frustum.BuildFrustumPlanes();

        var view          = viewCamera.GetViewMatrix();
        var cameraPosition = viewCamera.Position;

        ResetFrameStats(ref stats);

        scene.Lighting.Collect(scene.Entities);
        _lightBuffer.Update(scene.Lighting);

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
    private static void UploadGlobalUniforms(GLShader shader, Camera camera)
    {
        var projection = camera.GetProjectionMatrix();

        shader.SetUniform("uProjection", projection);

        // texture unit bindings
        shader.SetUniform("uAlbedoMap",    0);
        shader.SetUniform("uNormalMap",    1);
        shader.SetUniform("uRoughnessMap", 2);
        shader.SetUniform("uMetallicMap",  3);
        shader.SetUniform("uAOMap",        4);
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