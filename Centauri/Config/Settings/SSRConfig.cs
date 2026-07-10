namespace Centauri.Config;

using System.Text.Json.Serialization;

public sealed class SSRConfig : IJsonOnDeserialized
{
    [JsonPropertyName("enabled")]         public bool  Enabled         { get; set; } = true;
    [JsonPropertyName("intensity")]       public float Intensity       { get; set; } = 1.0f;   // reflection strength
    [JsonPropertyName("maxDistance")]     public float MaxDistance     { get; set; } = 15.0f;  // view-space ray length
    [JsonPropertyName("thickness")]       public float Thickness       { get; set; } = 1.0f;   // depth window for a hit
    [JsonPropertyName("maxSteps")]        public int   MaxSteps        { get; set; } = 48;     // linear march steps
    [JsonPropertyName("refineSteps")]     public int   RefineSteps     { get; set; } = 6;      // binary-search refinement
    [JsonPropertyName("roughnessCutoff")] public float RoughnessCutoff { get; set; } = 0.6f;   // fade SSR out above this
    [JsonPropertyName("halfResolution")]  public bool  HalfResolution  { get; set; } = false;  // trade reflection sharpness for speed (applied at startup/resize)
    [JsonPropertyName("silhouetteThreshold")] public float SilhouetteThreshold { get; set; } = 0.1f;
    [JsonPropertyName("temporalFeedback")] public float TemporalFeedback { get; set; } = 0.85f;

    [JsonIgnore] public float AuthoredIntensity       { get; private set; }
    [JsonIgnore] public float AuthoredMaxDistance     { get; private set; }
    [JsonIgnore] public float AuthoredThickness       { get; private set; }
    [JsonIgnore] public int   AuthoredMaxSteps        { get; private set; }
    [JsonIgnore] public int   AuthoredRefineSteps     { get; private set; }
    [JsonIgnore] public float AuthoredRoughnessCutoff { get; private set; }
    [JsonIgnore] public float AuthoredSilhouetteThreshold { get; private set; }
    [JsonIgnore] public float AuthoredTemporalFeedback { get; private set; }

    public SSRConfig() => OnDeserialized();

    public void OnDeserialized()
    {
        AuthoredIntensity       = Intensity;
        AuthoredMaxDistance     = MaxDistance;
        AuthoredThickness       = Thickness;
        AuthoredMaxSteps        = MaxSteps;
        AuthoredRefineSteps     = RefineSteps;
        AuthoredRoughnessCutoff = RoughnessCutoff;
        AuthoredSilhouetteThreshold = SilhouetteThreshold;
        AuthoredTemporalFeedback = TemporalFeedback;
    }
}
