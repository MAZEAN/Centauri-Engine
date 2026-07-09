namespace Centauri.Config;

using System.Text.Json.Serialization;

public class RenderConfig
{
    // Cache sizes not currently used
    [JsonPropertyName("textureCacheSize")] public int    TextureCacheSize { get; init; } = 128;
    [JsonPropertyName("modelCacheSize")]   public int    ModelCacheSize   { get; init; } = 64;
    [JsonPropertyName("shaderCacheSize")]  public int    ShaderCacheSize  { get; init; } = 32;
    [JsonPropertyName("scenePath")]        public string ScenePath        { get; init; } = "Loading/scene.json";
    [JsonPropertyName("defaultShader")]    public string DefaultShader    { get; init; } = "Assets/Shaders/shaderPBR";
}
