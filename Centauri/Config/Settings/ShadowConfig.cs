namespace Centauri.Config;

using System.Text.Json.Serialization;

public class ShadowConfig : IJsonOnDeserialized
{
    public readonly int MaxCascades = 4;

    [JsonPropertyName("enabled")]      public bool  Enabled      { get; set; } = true;
    [JsonPropertyName("size")]         public uint  Size         { get; set; } = 4096;
    [JsonPropertyName("distance")]     public float Distance     { get; set; } = 150f;
    [JsonPropertyName("depthBias")]    public float DepthBias    { get; set; } = 0.0005f;
    [JsonPropertyName("normalBias")]   public float NormalBias   { get; set; } = 1.5f;
    [JsonPropertyName("pcfRadius")]    public int   PcfRadius    { get; set; } = 2;
    [JsonPropertyName("cascadeCount")] public int   CascadeCount { get; set; } = 4;   // 2–4 typical
    [JsonPropertyName("splitLambda")]  public float SplitLambda  { get; set; } = 0.85f; // 0=uniform, 1=log

    // Resolution multiplier applied to every cascade after the first. Distant cascades cover a
    // much larger world-space area than the near one at the same physical resolution, so their
    // texel density is already far lower — rendering them at full size spends fill-rate/memory
    // on detail that isn't there. 1 = no reduction (identical to the old single-resolution
    // behavior); the near cascade (index 0) always renders at the full configured Size.
    [JsonPropertyName("farCascadeScale")] public float FarCascadeScale { get; set; } = 0.5f;

    [JsonPropertyName("contactHardening")]    public bool  ContactHardening    { get; set; } = true;
    [JsonPropertyName("lightSize")]           public float LightSize           { get; set; } = 0.02f;  // tan(sun half-angle) — world penumbra growth per unit occluder distance
    [JsonPropertyName("blockerSearchRadius")] public float BlockerSearchRadius { get; set; } = 5f;      // texels
    [JsonPropertyName("maxPenumbraRadius")]   public float MaxPenumbraRadius   { get; set; } = 24f;     // texels — caps softness/cost
    // Milliseconds to keep reusing the last cascade render while only wind-animated casters (not
    // a real sun/camera/config change) would otherwise force a redraw every frame — see
    // ShadowCache.CanReuse. Time-based (not frame-based) so the lag is the same real-world
    // duration regardless of framerate. 0 disables throttling (always redraw while animating).
    [JsonPropertyName("windThrottleMs")]      public float WindThrottleMs { get; set; } = 50f;

    [JsonIgnore] public bool DebugCascades { get; set; }

    [JsonIgnore] public uint  AuthoredSize                { get; private set; }
    [JsonIgnore] public float AuthoredDistance            { get; private set; }
    [JsonIgnore] public float AuthoredDepthBias           { get; private set; }
    [JsonIgnore] public float AuthoredNormalBias          { get; private set; }
    [JsonIgnore] public int   AuthoredPcfRadius           { get; private set; }
    [JsonIgnore] public int   AuthoredCascadeCount        { get; private set; }
    [JsonIgnore] public float AuthoredSplitLambda         { get; private set; }
    [JsonIgnore] public float AuthoredFarCascadeScale     { get; private set; }
    [JsonIgnore] public float AuthoredLightSize           { get; private set; }
    [JsonIgnore] public float AuthoredBlockerSearchRadius { get; private set; }
    [JsonIgnore] public float AuthoredMaxPenumbraRadius   { get; private set; }
    [JsonIgnore] public float AuthoredWindThrottleMs      { get; private set; }

    public ShadowConfig() => OnDeserialized();

    public void OnDeserialized()
    {
        AuthoredSize                = Size;
        AuthoredDistance            = Distance;
        AuthoredDepthBias           = DepthBias;
        AuthoredNormalBias          = NormalBias;
        AuthoredPcfRadius           = PcfRadius;
        AuthoredCascadeCount        = CascadeCount;
        AuthoredSplitLambda         = SplitLambda;
        AuthoredFarCascadeScale     = FarCascadeScale;
        AuthoredLightSize           = LightSize;
        AuthoredBlockerSearchRadius = BlockerSearchRadius;
        AuthoredMaxPenumbraRadius   = MaxPenumbraRadius;
        AuthoredWindThrottleMs      = WindThrottleMs;
    }
}
