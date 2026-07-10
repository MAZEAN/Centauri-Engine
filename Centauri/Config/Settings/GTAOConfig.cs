namespace Centauri.Config;

using System.Text.Json.Serialization;

public class GTAOConfig : IJsonOnDeserialized
{
    [JsonPropertyName("enabled")]    public bool  Enabled    { get; set; } = true;
    [JsonPropertyName("radius")]     public float Radius     { get; set; } = 0.5f;
    [JsonPropertyName("power")]      public float Power      { get; set; } = 1.5f;
    [JsonPropertyName("sliceCount")] public int   SliceCount { get; set; } = 2;
    [JsonPropertyName("stepCount")]  public int   StepCount  { get; set; } = 4;

    [JsonIgnore] public float AuthoredRadius     { get; private set; }
    [JsonIgnore] public float AuthoredPower      { get; private set; }
    [JsonIgnore] public int   AuthoredSliceCount { get; private set; }
    [JsonIgnore] public int   AuthoredStepCount  { get; private set; }

    public GTAOConfig() => OnDeserialized();

    public void OnDeserialized()
    {
        AuthoredRadius     = Radius;
        AuthoredPower      = Power;
        AuthoredSliceCount = SliceCount;
        AuthoredStepCount  = StepCount;
    }
}
