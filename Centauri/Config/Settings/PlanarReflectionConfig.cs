namespace Centauri.Config;

using System.Text.Json.Serialization;

public sealed class PlanarReflectionConfig
{
    [JsonPropertyName("enabled")]        public bool  Enabled        { get; set; } = true;
    [JsonPropertyName("reflectorEntity")] public string ReflectorEntity { get; set; } = "";   // if set, plane height auto-tracks this entity's top; else PlaneHeight is used
    [JsonPropertyName("planeHeight")]    public float PlaneHeight    { get; set; } = -1.5f;  // world-space Y of the reflector surface (fallback when ReflectorEntity is empty/not found)
    [JsonPropertyName("intensity")]      public float Intensity      { get; set; } = 1.0f;   // reflection strength (before Fresnel)
    [JsonPropertyName("blur")]           public float Blur           { get; set; } = 3.0f;
    [JsonPropertyName("distortion")]     public float Distortion     { get; set; } = 0.0f;   // surface-normal ripple offset (0 = mirror)
    [JsonPropertyName("halfResolution")] public bool  HalfResolution { get; set; } = true;   // render the mirror at half res (applied at startup/resize)
}
