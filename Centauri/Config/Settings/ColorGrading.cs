namespace Centauri.Config;

using System.Text.Json.Serialization;

public sealed class ColorGrading : IJsonOnDeserialized
{
    [JsonPropertyName("exposure")]   public float Exposure   { get; set; } = 1f;
    [JsonPropertyName("blackLevel")] public float BlackLevel { get; set; } = 0f;
    [JsonPropertyName("contrast")]   public float Contrast   { get; set; } = 1f;
    [JsonPropertyName("saturation")] public float Saturation { get; set; } = 1f;

    [JsonIgnore] public float AuthoredExposure   { get; private set; }
    [JsonIgnore] public float AuthoredBlackLevel { get; private set; }
    [JsonIgnore] public float AuthoredContrast   { get; private set; }
    [JsonIgnore] public float AuthoredSaturation { get; private set; }

    public ColorGrading() => OnDeserialized();

    public void OnDeserialized()
    {
        AuthoredExposure   = Exposure;
        AuthoredBlackLevel = BlackLevel;
        AuthoredContrast   = Contrast;
        AuthoredSaturation = Saturation;
    }
}
