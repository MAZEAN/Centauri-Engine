namespace Centauri.Config;

using System.Text.Json.Serialization;

public sealed class BloomConfig : IJsonOnDeserialized
{
    [JsonPropertyName("enabled")]   public bool  Enabled   { get; set; } = true;
    [JsonPropertyName("threshold")] public float Threshold { get; set; } = 1.0f;   // luma above which pixels bloom
    [JsonPropertyName("knee")]      public float Knee      { get; set; } = 0.5f;   // soft shoulder around the threshold
    [JsonPropertyName("intensity")] public float Intensity { get; set; } = 0.5f;   // additive strength into the scene
    [JsonPropertyName("radius")]    public float Radius    { get; set; } = 1.0f;   // upsample filter spread

    [JsonIgnore] public float AuthoredThreshold { get; private set; }
    [JsonIgnore] public float AuthoredKnee      { get; private set; }
    [JsonIgnore] public float AuthoredIntensity { get; private set; }
    [JsonIgnore] public float AuthoredRadius    { get; private set; }

    public BloomConfig() => OnDeserialized();

    public void OnDeserialized()
    {
        AuthoredThreshold = Threshold;
        AuthoredKnee      = Knee;
        AuthoredIntensity = Intensity;
        AuthoredRadius    = Radius;
    }
}
