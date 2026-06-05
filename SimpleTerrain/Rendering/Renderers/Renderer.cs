namespace SimpleTerrain.Rendering.Renderers;

using Silk.NET.OpenGL;
using Config;
using World;
using System.Numerics;
using Resources;

public class Renderer
{
    private readonly GL _gl;
    private readonly AppConfig _config;
    
    private readonly List<PointLight> _activePointLights = new();
    private readonly List<SpotLight>  _activeSpotLights  = new();

    private uint[] _boundTextures = null!;

    public Renderer(GL gl, AppConfig config)
    {
        _gl = gl;
        _config = config;
        
        InitializeTextureCache();
    }

    public void Render(Scene scene, float deltaTime)
    {
        var viewCamera = scene.GetActiveCamera(); 
        var cullingCamera = scene.GetPrimaryCamera();
        
        cullingCamera.Frustum.BuildFrustumPlanes();
        
        var view = viewCamera.GetViewMatrix();
        var cameraPosition = viewCamera.Position;
        
        bool lightingDirty = scene.Lighting.IsDirty;

        foreach (var (shader, entities) in scene.GetEntitiesByShader())
        {
            shader.Use();
            
            shader.SetUniform("uView", view);
            shader.SetUniform("uCameraPos", cameraPosition);
            
            UploadGlobalUniforms(shader, viewCamera);

            if (lightingDirty)
                UploadLighting(shader, scene.Lighting);

            foreach (var entity in entities)
            {
                if (scene.EnableCulling && !cullingCamera.Frustum.IsVisible(entity))
                    continue;

                var mat = entity.Material;
                
                BindMaterialTextures(mat);
                UploadMaterialFlags(shader, mat);
                UploadTransform(shader, entity);
                UploadMaterialProperties(shader, mat);

                foreach (var mesh in entity.Model.Meshes)
                {
                    mesh.Bind();
                    unsafe
                    {
                        _gl.DrawElements(
                            PrimitiveType.Triangles,
                            mesh.IndexCount,
                            DrawElementsType.UnsignedInt,
                            (void*)0
                        );
                    }
                }
            }
        }

        if (lightingDirty)
            scene.Lighting.ClearDirty();
    }
    
    // -----------------------------
    // Material + Texture handling
    // -----------------------------
    private void BindMaterialTextures(Material mat)
    {
        BindTexture(mat.Albedo,    TextureUnit.Texture0);
        BindTexture(mat.Normal,    TextureUnit.Texture1);
        BindTexture(mat.Roughness, TextureUnit.Texture2);
        BindTexture(mat.Metallic,  TextureUnit.Texture3);
        BindTexture(mat.AO,        TextureUnit.Texture4);
    }

    private void BindTexture(GLTexture? tex, TextureUnit slot)
    {
        int index = (int)slot - (int)TextureUnit.Texture0;
        uint handle = tex?.Handle ?? 0;
        
        if (index < 0 || index >= _boundTextures.Length)
            throw new Exception($"Texture slot {slot} exceeds supported range.");

        if (_boundTextures[index] == handle)
            return;

        _gl.ActiveTexture(slot);
        _gl.BindTexture(TextureTarget.Texture2D, handle);

        _boundTextures[index] = handle;
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
    }
    
    // -----------------------------
    // Material
    // -----------------------------
    private void UploadMaterialFlags(GLShader shader, Material mat)
    {
        shader.SetUniform("uHasAlbedo",    mat.Albedo    != null ? 1 : 0);
        shader.SetUniform("uHasNormal",    mat.Normal    != null ? 1 : 0);
        shader.SetUniform("uHasRoughness", mat.Roughness != null ? 1 : 0);
        shader.SetUniform("uHasMetallic",  mat.Metallic  != null ? 1 : 0);
        shader.SetUniform("uHasAO",        mat.AO        != null ? 1 : 0);
    }

    private void UploadMaterialProperties(GLShader shader, Material mat)
    {
        shader.SetUniform("uRoughnessValue", mat.RoughnessValue);
        shader.SetUniform("uMetallicValue",  mat.MetallicValue);
        shader.SetUniform("uColor",          mat.Color);
        shader.SetUniform("uUvScale",        mat.UvScale);
        shader.SetUniform("uUvOffset",       mat.UvOffset);
    }
    
    // -----------------------------
    // Transform
    // -----------------------------
    private void UploadTransform(GLShader shader, Entity entity)
    {
        var model = entity.Transform.WorldMatrix;

        shader.SetUniform("uModel", model);

        if (Matrix4x4.Invert(model, out var invModel))
            shader.SetUniformMat3x3("uNormalMatrix", Matrix4x4.Transpose(invModel));
        else
            shader.SetUniformMat3x3("uNormalMatrix", Matrix4x4.Transpose(model));
    }
    
    // -----------------------------
    // Lighting
    // -----------------------------
    private void UploadLighting(GLShader shader, LightingSystem lights)
    {
        UploadDirectionalLight(shader, lights);
        UploadPointLights(shader, lights);
        UploadSpotLights(shader, lights);
    }

    private void UploadDirectionalLight(GLShader shader, LightingSystem lights)
    {
        DirectionalLight? dir = null;

        foreach (var l in lights.DirectionalLights)
        {
            if (!l.Enabled) continue;
            dir = l;
            break;
        }

        if (dir == null)
            return;

        shader.SetUniform("uDirLight.direction", dir.Direction);
        shader.SetUniform("uDirLight.color",     dir.Color);
        shader.SetUniform("uDirLight.intensity", dir.Intensity);
    }

    private void UploadPointLights(GLShader shader, LightingSystem lights)
    {
        _activePointLights.Clear();

        foreach (var l in lights.PointLights)
        {
            if (l.Enabled)
                _activePointLights.Add(l);
        }

        int count = _activePointLights.Count;
        shader.SetUniform("uPointLightCount", count);
        
        for (int i = 0; i < count; i++)
        {
            var light = _activePointLights[i];

            shader.SetUniform($"uPointLights[{i}].position",  light.Position);
            shader.SetUniform($"uPointLights[{i}].color",     light.Color);
            shader.SetUniform($"uPointLights[{i}].intensity", light.Intensity);
            shader.SetUniform($"uPointLights[{i}].constant",  light.Constant);
            shader.SetUniform($"uPointLights[{i}].linear",    light.Linear);
            shader.SetUniform($"uPointLights[{i}].quadratic", light.Quadratic);
        }
    }

    private void UploadSpotLights(GLShader shader, LightingSystem lights)
    {
        _activeSpotLights.Clear();

        foreach (var l in lights.SpotLights)
        {
            if (l.Enabled)
                _activeSpotLights.Add(l);
        }

        int count = _activeSpotLights.Count;
        shader.SetUniform("uSpotLightCount", count);

        for (int i = 0; i < count; i++)
        {
            var light = _activeSpotLights[i];

            shader.SetUniform($"uSpotLights[{i}].position",  light.Position);
            shader.SetUniform($"uSpotLights[{i}].direction", light.Direction);
            shader.SetUniform($"uSpotLights[{i}].color",     light.Color);
            shader.SetUniform($"uSpotLights[{i}].intensity", light.Intensity);
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
}