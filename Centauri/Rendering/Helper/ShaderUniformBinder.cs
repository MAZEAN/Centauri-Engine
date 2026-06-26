namespace Centauri.Rendering.Helper;

using Config;
using World;
using Graphics.Resources;
using Graphics.Resources.Materials;
using IBL;
using Shadows;

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

    public void UploadGlobals(GLShader shader, Camera camera, bool iblActive, float iblIntensityScale, bool ssaoActive)
    {
        shader.SetUniform("uProjection", camera.GetProjectionMatrix());

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
        shader.SetUniform("uHasIBL", iblActive ? 1 : 0);
        shader.SetUniform("uMaxReflectionLod", (float)_ibl.MaxReflectionLod);
        shader.SetUniform("uIblIntensity", _config.IBLConfig.IblIntensity * iblIntensityScale);
        
        // screen-space AO
        shader.SetUniform("uSsaoMap", 9);
        shader.SetUniform("uHasSSAO", ssaoActive ? 1 : 0);

        // CSM bindings
        shader.SetUniform("uShadowMap", 8);
        shader.SetUniform("uHasShadow", _shadows.Active ? 1 : 0);
        shader.SetUniform("uShowCascades", _config.Shadows.DebugCascades ? 1 : 0);

        if (!_shadows.Active) return;

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

    public static void UploadMaterial(GLShader shader, Material mat)
    {
        shader.SetUniform("uHasAlbedo",    mat.Albedo    != null ? 1 : 0);
        shader.SetUniform("uHasNormal",    mat.Normal    != null ? 1 : 0);
        shader.SetUniform("uHasRoughness", mat.Roughness != null ? 1 : 0);
        shader.SetUniform("uHasMetallic",  mat.Metallic  != null ? 1 : 0);

        shader.SetUniform("uRoughnessValue", mat.RoughnessValue);
        shader.SetUniform("uMetallicValue",  mat.MetallicValue);
        shader.SetUniform("uColor",          mat.Color);
        shader.SetUniform("uFoliage",        mat.TwoSided ? 1 : 0);
    }
}