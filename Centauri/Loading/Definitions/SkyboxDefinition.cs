namespace Centauri.Loading;

using System.Text.Json.Serialization;

public class SkyboxDefinition
{
    [JsonPropertyName("name")]     public string Name     { get; set; } = "Skybox";
    [JsonPropertyName("panorama")] public string Panorama { get; set; } = "";
    [JsonPropertyName("exposure")] public float  Exposure { get; set; } = 1.0f;
    [JsonPropertyName("blackLevel")] public float BlackLevel { get; set; } = 0.0f;
    [JsonPropertyName("active")]   public bool   Active   { get; set; }
}