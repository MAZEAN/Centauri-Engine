namespace Centauri.Rendering.Helper;

using Config;
using World;
using Graphics.Resources;
using Graphics.Resources.Materials;
using IBL;
using Shadows;
using Utils.Misc;

// Packs the uniforms a lit shader needs: the per-shader globals (texture-unit slots,
// IBL, CSM) and the per-entity material/transform values. Pure uniform uploads — the
// actual texture binding lives in TextureBinder / the IBL+shadow binds.
public sealed class ShaderUniformBinder
{
    private readonly AppConfig _config;
    private readonly IBLBaker _ibl;
    private readonly ShadowMapper _shadows;

    public ShaderUniformBinder(AppConfig config, IBLBaker ibl, ShadowMapper shadows)
    {
        _config  = config;
        _ibl     = ibl;
        _shadows = shadows;
    }

    public void UploadGlobals(GLShader shader, Camera camera, bool iblActive, float iblIntensityScale,
        bool gtaoActive, bool cheapShading = false)
    {
        shader.SetUniform("uProjection", camera.GetProjectionMatrix());
        shader.SetUniform("uCheapShading", cheapShading ? 1 : 0);
        shader.SetUniform("uFoliageAlphaCutoff", _config.Foliage.AlphaCutoff);
        UploadWind(shader, _config.Foliage);

        UploadTextureSlots(shader);
        UploadIbl(shader, iblActive, iblIntensityScale);
        UploadGtao(shader, gtaoActive);
        UploadShadow(shader);
    }

    private static void UploadTextureSlots(GLShader shader)
    {
        shader.SetUniform("uAlbedoMap",    0);
        shader.SetUniform("uNormalMap",    1);
        shader.SetUniform("uRoughnessMap", 2);
        shader.SetUniform("uMetallicMap",  3);
        shader.SetUniform("uAOMap",        4);
    }
    
    private void UploadIbl(GLShader shader, bool iblActive, float iblIntensityScale)
    {
        shader.SetUniform("uIrradianceMap", 5);
        shader.SetUniform("uPrefilterMap",  6);
        shader.SetUniform("uBrdfLUT",       7);
        shader.SetUniform("uHasIBL", iblActive ? 1 : 0);
        shader.SetUniform("uMaxReflectionLod", (float)_ibl.MaxReflectionLod);
        shader.SetUniform("uIblIntensity", _config.IBL.IblIntensity * iblIntensityScale);
    }
    
    private static void UploadGtao(GLShader shader, bool gtaoActive)
    {
        shader.SetUniform("uGtaoMap", 9);
        shader.SetUniform("uHasGtao", gtaoActive ? 1 : 0);
    }

    private void UploadShadow(GLShader shader)
    {
        shader.SetUniform("uShadowMapNear", 8);
        shader.SetUniform("uShadowMapNearRaw", 10);
        shader.SetUniform("uShadowMapFar", 11);
        shader.SetUniform("uShadowMapFarRaw", 12);
        shader.SetUniform("uHasShadow", _shadows.Active ? 1 : 0);
        shader.SetUniform("uShowCascades", _config.Shadows.DebugCascades ? 1 : 0);
        
        if (!_shadows.Active) return;

        shader.SetUniform("uCascadeCount", _shadows.Cascades.Length);
        shader.SetUniform("uShadowBias", _config.Shadows.DepthBias);
        shader.SetUniform("uNormalBias", _config.Shadows.NormalBias);
        shader.SetUniform("uPcfRadius",  _config.Shadows.PcfRadius);
        
        shader.SetUniform("uPcss",         _config.Shadows.ContactHardening ? 1 : 0);
        shader.SetUniform("uLightSize",    _config.Shadows.LightSize);
        shader.SetUniform("uBlockerRadius", _config.Shadows.BlockerSearchRadius);
        shader.SetUniform("uMaxPenumbra",  _config.Shadows.MaxPenumbraRadius);
    }

    public static void UploadMaterial(GLShader shader, Material mat)
    {
        shader.SetUniform("uHasAlbedo",    mat.Albedo    != null ? 1 : 0);
        shader.SetUniform("uHasNormal",    mat.Normal    != null ? 1 : 0);
        shader.SetUniform("uHasRoughness", mat.Roughness != null ? 1 : 0);
        shader.SetUniform("uHasMetallic",  mat.Metallic  != null ? 1 : 0);
        shader.SetUniform("uHasAO",        mat.AO        != null ? 1 : 0);

        shader.SetUniform("uRoughnessScalar", mat.RoughnessScalar);
        shader.SetUniform("uMetallicScalar",  mat.MetallicScalar);
        shader.SetUniform("uTranslucency",    mat.Translucency);
        shader.SetUniform("uColor",           mat.Color);
        shader.SetUniform("uFoliage",         mat.TwoSided ? 1 : 0);
        shader.SetUniform("uWind",            mat.Wind ? 1 : 0);
        shader.SetUniform("uTriplanar",       mat.Triplanar ? 1 : 0);
        shader.SetUniform("uTriplanarScale",  mat.TriplanarScale);
    }
    
    public static void UploadWind(GLShader shader, FoliageConfig foliage)
    {
        shader.SetUniform("uTime",         Time.Now);
        shader.SetUniform("uWindStrength", foliage.WindEnabled ? foliage.WindStrength : 0f);
        shader.SetUniform("uWindSpeed",    foliage.WindSpeed);
        shader.SetUniform("uWindDir",      foliage.WindDirectionVector);
    }
}