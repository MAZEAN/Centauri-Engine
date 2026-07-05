namespace Centauri.Config;

using System.Text.Json.Serialization;

// Eye adaptation: AutoExposurePass measures the resolved HDR scene's average log-luminance
// each frame and the tonemap shader derives its own exposure multiplier from it (on top of,
// not instead of, ColorGrading.Exposure — that stays a manual EV-compensation dial). Off by
// default: existing scenes keep their fixed manual exposure exactly as before until enabled.
public sealed class AutoExposureConfig : IJsonOnDeserialized
{
    [JsonPropertyName("enabled")]     public bool  Enabled     { get; set; } = true;
    [JsonPropertyName("keyValue")]    public float KeyValue    { get; set; } = 0.18f;  // "middle grey" target
    [JsonPropertyName("adaptSpeed")]  public float AdaptSpeed  { get; set; } = 1.5f;   // higher = adapts faster
    [JsonPropertyName("minExposure")] public float MinExposure { get; set; } = 0.1f;
    [JsonPropertyName("maxExposure")] public float MaxExposure { get; set; } = 8.0f;

    [JsonIgnore] public float AuthoredKeyValue    { get; private set; }
    [JsonIgnore] public float AuthoredAdaptSpeed  { get; private set; }
    [JsonIgnore] public float AuthoredMinExposure { get; private set; }
    [JsonIgnore] public float AuthoredMaxExposure { get; private set; }

    public AutoExposureConfig() => OnDeserialized();

    public void OnDeserialized()
    {
        AuthoredKeyValue    = KeyValue;
        AuthoredAdaptSpeed  = AdaptSpeed;
        AuthoredMinExposure = MinExposure;
        AuthoredMaxExposure = MaxExposure;
    }
}