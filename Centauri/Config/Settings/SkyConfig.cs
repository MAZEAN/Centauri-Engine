namespace Centauri.Config;

using System.Text.Json.Serialization;

// Procedural atmosphere (Preetham et al. analytic model). When Procedural is on, the skybox
// shader computes the sky color from the sun's live direction + turbidity instead of sampling
// the panorama — so the sky (and its sun) stay in sync with a moving DirectionalLight through
// the day, with no baked-in photo to go stale. Off by default: existing textured skyboxes
// render exactly as before.
public sealed class SkyConfig : IJsonOnDeserialized
{
    [JsonPropertyName("procedural")] public bool  Procedural { get; set; } = false;
    [JsonPropertyName("turbidity")]  public float Turbidity  { get; set; } = 2.5f;  // 1=clear alpine, 2-4=clear day, 6+=hazy
    [JsonPropertyName("intensity")]  public float Intensity  { get; set; } = 1.0f;  // scales sky radiance into the exposure/tonemap range

    [JsonIgnore] public float AuthoredTurbidity { get; private set; }
    [JsonIgnore] public float AuthoredIntensity { get; private set; }

    public SkyConfig() => OnDeserialized();

    public void OnDeserialized()
    {
        AuthoredTurbidity = Turbidity;
        AuthoredIntensity = Intensity;
    }
}