namespace Centauri.Config;

using System.Numerics;
using System.Text.Json.Serialization;

// Foliage-wide settings: wind sway (materials opt in via the per-material `wind` flag) and the
// alpha-tested cutout shared by the lit pass (shaderPBR.frag) and ZPrepass (zprepass.frag).
public sealed class FoliageConfig : IJsonOnDeserialized
{
    [JsonPropertyName("windEnabled")]   public bool  WindEnabled   { get; set; } = true;
    [JsonPropertyName("windStrength")]  public float WindStrength  { get; set; } = 0.06f;  // sway amplitude
    [JsonPropertyName("windSpeed")]     public float WindSpeed     { get; set; } = 1.6f;    // oscillation frequency
    [JsonPropertyName("windDirection")] public float WindDirection { get; set; } = 37f;     // heading in XZ, degrees

    // Alpha-tested cutout threshold for the main lit pass AND ZPrepass — these two MUST stay
    // equal. ZPrepass's depth is what Forward's DepthFunc(Lequal)/no-write trusts as
    // authoritative; a mismatch leaves a gap of fragments the lit pass shades but ZPrepass never
    // wrote real depth for, which don't depth-sort against each other and show as flickery,
    // arbitrarily-ordered noise at leaf edges. Lower = more of the texture's soft alpha edge
    // renders (softer look, wider partial-coverage band for alpha-to-coverage to dither);
    // higher = harder cutout, less edge fringing but blockier silhouettes.
    [JsonPropertyName("alphaCutoff")] public float AlphaCutoff { get; set; } = 0.05f;

    [JsonIgnore] public float AuthoredWindStrength  { get; private set; }
    [JsonIgnore] public float AuthoredWindSpeed     { get; private set; }
    [JsonIgnore] public float AuthoredWindDirection { get; private set; }
    [JsonIgnore] public float AuthoredAlphaCutoff   { get; private set; }

    // unit XZ heading the wind blows toward
    [JsonIgnore]
    public Vector2 WindDirectionVector
    {
        get
        {
            var r = WindDirection * MathF.PI / 180f;
            return new Vector2(MathF.Cos(r), MathF.Sin(r));
        }
    }

    // foliage actually sways only when enabled with non-zero strength and speed
    [JsonIgnore] public bool WindAnimating => WindEnabled && WindStrength > 0f && WindSpeed > 0f;

    public FoliageConfig() => OnDeserialized();

    public void OnDeserialized()
    {
        AuthoredWindStrength  = WindStrength;
        AuthoredWindSpeed     = WindSpeed;
        AuthoredWindDirection = WindDirection;
        AuthoredAlphaCutoff   = AlphaCutoff;
    }
}
