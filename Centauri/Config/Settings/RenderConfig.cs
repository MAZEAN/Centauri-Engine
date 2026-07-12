namespace Centauri.Config;

using System.Text.Json.Serialization;

public class RenderConfig
{
    // Cache sizes not currently used
    [JsonPropertyName("textureCacheSize")] public int    TextureCacheSize { get; init; } = 128;
    [JsonPropertyName("modelCacheSize")]   public int    ModelCacheSize   { get; init; } = 64;
    [JsonPropertyName("shaderCacheSize")]  public int    ShaderCacheSize  { get; init; } = 32;
    [JsonPropertyName("scenePath")]        public string ScenePath        { get; init; } = "Loading/scene.json";
    [JsonPropertyName("defaultShader")]    public string DefaultShader    { get; init; } = "Shaders/shaderPBR";

    // Fraction of the window's framebuffer resolution the scene (HDR target, prepass, GTAO,
    // SSR, bloom/autoexposure/TAA) actually renders at — everything downstream of the lit pass
    // scales together so their textures stay dimensionally consistent (GTAO/SSR sample the
    // prepass G-buffer by UV, not absolute pixel count, so this only requires them to share the
    // same base resolution, not any particular one). Only the final tonemap draw stays pinned to
    // the window's native resolution: it already samples the (possibly smaller) scene color
    // texture through a linear-filtered sampler into a full-size viewport, so upscaling falls
    // out of that draw for free — no dedicated upscale pass needed. 1 = full native resolution,
    // identical to the old unscaled behavior; below 1 trades sharpness for fill-rate, the
    // standard lever for catching up a fill-bound frame on a weak GPU.
    [JsonPropertyName("renderScale")]      public float  RenderScale      { get; set; } = 1f;
}
