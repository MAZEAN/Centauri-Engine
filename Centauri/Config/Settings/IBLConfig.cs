namespace Centauri.Config;

using System.Text.Json.Serialization;

public class IBLConfig : IJsonOnDeserialized
{
    [JsonPropertyName("iblIntensity")]    public float IblIntensity { get; set; } = 0.3f;
    [JsonPropertyName("maxRadiance")]     public float MaxRadiance { get; init; } = 10f;
    [JsonPropertyName("envSize")]         public uint EnvSize { get; init; } = 512;
    [JsonPropertyName("irradianceSize")]  public uint IrradianceSize { get; init; } = 64;
    [JsonPropertyName("prefilterSize")]   public uint PrefilterSize { get; init; }  = 128;
    [JsonPropertyName("prefilterMips")]   public int PrefilterMips { get; init; }  = 5;
    [JsonPropertyName("brdfSize")]        public uint BrdfSize { get; init; }  = 512;

    [JsonIgnore] public float AuthoredIblIntensity { get; private set; }

    public IBLConfig() => OnDeserialized();

    public void OnDeserialized()
    {
        AuthoredIblIntensity =  IblIntensity;
    }
}
