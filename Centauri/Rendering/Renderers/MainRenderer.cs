namespace Centauri.Rendering.Renderers;

using Silk.NET.OpenGL;
using System.Numerics;

using Config;
using World;
using Resources;
using Systems;
using Utils.Misc;

public class MainRenderer
{
    private readonly GL _gl;
    private readonly AppConfig _config;
    
    private const int MaxPointLights = 16;
    private const int MaxSpotLights  = 16;

    private uint[] _boundTextures = null!;

    public MainRenderer(GL gl, AppConfig config)
    {
        _gl = gl;
        _config = config;
        
        InitializeTextureCache();
    }

    public void Render(Scene scene, float deltaTime, ref FrameStats stats)
    {
        var viewCamera    = scene.GetActiveCamera();
        var cullingCamera = scene.GetPrimaryCamera();
        
        cullingCamera.Frustum.BuildFrustumPlanes();

        var view          = viewCamera.GetViewMatrix();
        var cameraPosition = viewCamera.Position;

        ResetFrameStats(scene, ref stats);

        scene.Lighting.Collect(scene.Entities);

        foreach (var (shader, entities) in scene.GetEntitiesByShader())
        {
            shader.Use();

            shader.SetUniform("uView",      view);
            shader.SetUniform("uCameraPos", cameraPosition);

            UploadGlobalUniforms(shader, viewCamera);
            UploadLighting(shader, scene.Lighting);

            foreach (var entity in entities.Where(entity => entity.Enabled))
            {
                if (entity.Model is not { } model || entity.Material is not { } mat) continue;

                if (_config.Debug.EnableCulling && !cullingCamera.Frustum.IsVisibleAABB(entity.GetWorldBounds()))
                {
                    stats.CulledEntities++; 
                    continue;
                }
                    
                
                stats.TextureBinds += BindMaterialTextures(mat);
                stats.DrawnEntities++;
                
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
        }
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
    private static void UploadLighting(GLShader shader, LightingSystem lights)
    {
        UploadDirectionalLight(shader, lights);
        UploadPointLights(shader, lights);
        UploadSpotLights(shader, lights);
    }

    private static void UploadDirectionalLight(GLShader shader, LightingSystem lights)
    {
        if (lights.DirectionalLights.Count == 0)
            return;

        var dir = lights.DirectionalLights[0];
        shader.SetUniform("uDirLight.direction", dir.Direction);
        shader.SetUniform("uDirLight.color",     dir.Color);
        shader.SetUniform("uDirLight.intensity", dir.Intensity);
    }

    private static void UploadPointLights(GLShader shader, LightingSystem lights)
    {
        var count = Math.Min(lights.PointLights.Count, MaxPointLights);
        shader.SetUniform("uPointLightCount", count);

        for (var i = 0; i < count; i++)
        {
            var active = lights.PointLights[i];
            var light  = active.Light;

            shader.SetUniform($"uPointLights[{i}].position",  active.Position);
            shader.SetUniform($"uPointLights[{i}].color",     light.Color);
            shader.SetUniform($"uPointLights[{i}].intensity", light.Intensity);
            shader.SetUniform($"uPointLights[{i}].constant",  light.Constant);
            shader.SetUniform($"uPointLights[{i}].linear",    light.Linear);
            shader.SetUniform($"uPointLights[{i}].quadratic", light.Quadratic);
        }
    }

    private static void UploadSpotLights(GLShader shader, LightingSystem lights)
    {
        var count = Math.Min(lights.SpotLights.Count, MaxSpotLights);
        shader.SetUniform("uSpotLightCount", count);

        for (var i = 0; i < count; i++)
        {
            var active = lights.SpotLights[i];
            var light  = active.Light;

            shader.SetUniform($"uSpotLights[{i}].position",    active.Position);
            shader.SetUniform($"uSpotLights[{i}].direction",   light.Direction);
            shader.SetUniform($"uSpotLights[{i}].color",       light.Color);
            shader.SetUniform($"uSpotLights[{i}].intensity",   light.Intensity);
            shader.SetUniform($"uSpotLights[{i}].innerCutoff", MathF.Cos(light.InnerCutoff * MathF.PI / 180f));
            shader.SetUniform($"uSpotLights[{i}].outerCutoff", MathF.Cos(light.OuterCutoff * MathF.PI / 180f));
        }
    }

    private void InitializeTextureCache()
    {
        _gl.GetInteger(GLEnum.MaxTextureImageUnits, out var maxUnits);

        _boundTextures = new uint[maxUnits];
        Array.Fill(_boundTextures, uint.MaxValue);
    }
    
    private static void ResetFrameStats(Scene scene, ref FrameStats stats)
    {
        stats.DrawnEntities  = 0;
        stats.CulledEntities = 0;
        stats.DrawCalls      = 0;
        stats.TextureBinds   = 0;
        stats.TotalIndices   = 0;
        stats.TotalVertices  = 0;
    }
}