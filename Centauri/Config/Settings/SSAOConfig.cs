namespace Centauri.Config;

using System.Text.Json.Serialization;

public class SSAOConfig : IJsonOnDeserialized
{
    [JsonPropertyName("enabled")]     public bool  Enabled     { get; set; } = true;
    [JsonPropertyName("radius")]      public float Radius      { get; set; } = 0.5f;
    [JsonPropertyName("bias")]        public float Bias        { get; set; } = 0.025f;
    [JsonPropertyName("power")]       public float Power       { get; set; } = 1.5f;
    [JsonPropertyName("sampleCount")] public int   SampleCount { get; set; } = 32;

    [JsonIgnore] public float AuthoredRadius      { get; private set; }
    [JsonIgnore] public float AuthoredBias        { get; private set; }
    [JsonIgnore] public float AuthoredPower       { get; private set; }
    [JsonIgnore] public int   AuthoredSampleCount { get; private set; }

    public SSAOConfig() => OnDeserialized();

    public void OnDeserialized()
    {
        AuthoredRadius      = Radius;
        AuthoredBias        = Bias;
        AuthoredPower       = Power;
        AuthoredSampleCount = SampleCount;
    }
}
