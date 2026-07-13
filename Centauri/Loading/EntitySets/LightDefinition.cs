namespace Centauri.Loading;

using System.Text.Json.Serialization;

public class LightDefinition
{
    [JsonPropertyName("type")]        public string  Type      { get; set; } = "point"; // directional|point|spot
    [JsonPropertyName("color")]       public float[] Color     { get; set; } = [1f, 1f, 1f];
    [JsonPropertyName("intensity")]   public float   Intensity { get; set; } = 1.0f;
    [JsonPropertyName("enabled")]     public bool    Enabled   { get; set; } = true;

    // directional + spot
    [JsonPropertyName("direction")]   public float[] Direction { get; set; } = [0f, -1f, 0f];

    // point
    [JsonPropertyName("constant")]    public float Constant  { get; set; } = 1.0f;
    [JsonPropertyName("linear")]      public float Linear    { get; set; } = 0.09f;
    [JsonPropertyName("quadratic")]   public float Quadratic { get; set; } = 0.032f;

    // spot
    [JsonPropertyName("innerCutoff")] public float InnerCutoff { get; set; } = 12.5f;
    [JsonPropertyName("outerCutoff")] public float OuterCutoff { get; set; } = 17.5f;
}