namespace Centauri.Config;

using System.Text.Json.Serialization;

public class RenderConfig : IJsonOnDeserialized
{
    // Cache sizes not currently used
    [JsonPropertyName("textureCacheSize")] public int    TextureCacheSize { get; init; } = 128;
    [JsonPropertyName("modelCacheSize")]   public int    ModelCacheSize   { get; init; } = 64;
    [JsonPropertyName("shaderCacheSize")]  public int    ShaderCacheSize  { get; init; } = 32;
    [JsonPropertyName("scenePath")]        public string ScenePath        { get; init; } = "Loading/scene.json";
    [JsonPropertyName("defaultShader")]    public string DefaultShader    { get; init; } = "Shaders/shaderPBR";
    [JsonPropertyName("foliageAlphaCutoff")] public float FoliageAlphaCutoff { get; set; } = 0.3f;
    [JsonIgnore] public float AuthoredFoliageAlphaCutoff { get; private set; }

    public RenderConfig() => OnDeserialized();

    public void OnDeserialized() => AuthoredFoliageAlphaCutoff = FoliageAlphaCutoff;
}
