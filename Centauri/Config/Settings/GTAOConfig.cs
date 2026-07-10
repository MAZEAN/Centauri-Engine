namespace Centauri.Config;

using System.Text.Json.Serialization;

public class GTAOConfig : IJsonOnDeserialized
{
    [JsonPropertyName("enabled")]    public bool  Enabled    { get; set; } = true;
    [JsonPropertyName("radius")]     public float Radius     { get; set; } = 1.2f;
    [JsonPropertyName("power")]      public float Power      { get; set; } = 1.2f;
    [JsonPropertyName("sliceCount")] public int   SliceCount { get; set; } = 3;
    [JsonPropertyName("stepCount")]  public int   StepCount  { get; set; } = 8;
    [JsonPropertyName("temporalFeedback")] public float TemporalFeedback { get; set; } = 0.85f;

    [JsonIgnore] public float AuthoredRadius           { get; private set; }
    [JsonIgnore] public float AuthoredPower            { get; private set; }
    [JsonIgnore] public int   AuthoredSliceCount        { get; private set; }
    [JsonIgnore] public int   AuthoredStepCount         { get; private set; }
    [JsonIgnore] public float AuthoredTemporalFeedback { get; private set; }

    public GTAOConfig() => OnDeserialized();

    public void OnDeserialized()
    {
        AuthoredRadius           = Radius;
        AuthoredPower            = Power;
        AuthoredSliceCount       = SliceCount;
        AuthoredStepCount        = StepCount;
        AuthoredTemporalFeedback = TemporalFeedback;
    }
}
