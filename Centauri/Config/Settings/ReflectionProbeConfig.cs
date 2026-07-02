namespace Centauri.Config;

using System.Text.Json.Serialization;

public sealed class ReflectionProbeConfig : IJsonOnDeserialized
{
    [JsonPropertyName("enabled")]    public bool    Enabled    { get; set; } = true;
    [JsonPropertyName("position")]   public float[] Position   { get; set; } = [0f, 2f, -3f];
    [JsonPropertyName("resolution")] public uint    Resolution { get; set; } = 128;   // cubemap face size
    [JsonPropertyName("intensity")]  public float   Intensity  { get; set; } = 0.3f;
    [JsonPropertyName("boxCenter")]  public float[] BoxCenter  { get; set; } = [0f, -1f, -3f];
    [JsonPropertyName("boxSize")]    public float[] BoxSize    { get; set; } = [20f, 3f, 20f];   // half-extents
    [JsonPropertyName("boxFalloff")] public float   BoxFalloff { get; set; } = 1.0f;

    [JsonIgnore] public bool RebakeRequested { get; set; }
    [JsonIgnore] public bool Baked           { get; set; }

    [JsonIgnore] public float AuthoredIntensity { get; private set; }
    [JsonIgnore] public float AuthoredBoxFalloff { get; private set; }

    public ReflectionProbeConfig() => OnDeserialized();

    public void OnDeserialized()
    {
        AuthoredIntensity = Intensity;
        AuthoredBoxFalloff = BoxFalloff;
    }
}
