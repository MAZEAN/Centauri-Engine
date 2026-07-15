namespace Centauri.Config;

using System.Text.Json.Serialization;

// Shadow maps for spot lights individually opted in via SpotLight.CastsShadow — see
// Docs/Documentation/LocalShadows.md. Independent of ShadowConfig (the directional/CSM sun
// shadow): this is off having no effect on that, and vice versa.
public sealed class SpotShadowConfig : IJsonOnDeserialized
{
    // Hard cap on simultaneously shadow-casting spot lights — a real GPU resource (one
    // Texture2DArray layer + a redraw pass each), not a soft UI limit, mirroring
    // ShadowConfig.MaxCascades. When more than this many lights have CastsShadow set, the
    // nearest-to-camera MaxShadowSpots win each frame (SpotShadowMapper.SelectActive).
    public readonly int MaxShadowSpots = 4;

    [JsonPropertyName("enabled")]    public bool  Enabled    { get; set; } = true;
    [JsonPropertyName("size")]       public uint  Size       { get; set; } = 1024;
    [JsonPropertyName("depthBias")]  public float DepthBias  { get; set; } = 0.0015f;
    [JsonPropertyName("normalBias")] public float NormalBias { get; set; } = 2.5f;
    [JsonPropertyName("pcfRadius")]  public int   PcfRadius  { get; set; } = 2;

    [JsonIgnore] public uint  AuthoredSize       { get; private set; }
    [JsonIgnore] public float AuthoredDepthBias  { get; private set; }
    [JsonIgnore] public float AuthoredNormalBias { get; private set; }
    [JsonIgnore] public int   AuthoredPcfRadius  { get; private set; }

    public SpotShadowConfig() => OnDeserialized();

    public void OnDeserialized()
    {
        AuthoredSize       = Size;
        AuthoredDepthBias  = DepthBias;
        AuthoredNormalBias = NormalBias;
        AuthoredPcfRadius  = PcfRadius;
    }
}
