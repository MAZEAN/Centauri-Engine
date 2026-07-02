namespace Centauri.Config;

using System.Numerics;
using System.Text.Json.Serialization;

public sealed class WindConfig : IJsonOnDeserialized
{
    [JsonPropertyName("enabled")]   public bool  Enabled   { get; set; } = true;
    [JsonPropertyName("strength")]  public float Strength  { get; set; } = 0.06f;   // sway amplitude
    [JsonPropertyName("speed")]     public float Speed     { get; set; } = 1.6f;    // oscillation frequency
    [JsonPropertyName("direction")] public float Direction { get; set; } = 37f;     // heading in XZ, degrees

    [JsonIgnore] public float AuthoredStrength  { get; private set; }
    [JsonIgnore] public float AuthoredSpeed     { get; private set; }
    [JsonIgnore] public float AuthoredDirection { get; private set; }

    // unit XZ heading the wind blows toward
    [JsonIgnore]
    public Vector2 DirectionVector
    {
        get
        {
            var r = Direction * MathF.PI / 180f;
            return new Vector2(MathF.Cos(r), MathF.Sin(r));
        }
    }

    // foliage actually moves only when enabled with non-zero strength and speed
    [JsonIgnore] public bool Animating => Enabled && Strength > 0f && Speed > 0f;

    public WindConfig() => OnDeserialized();

    public void OnDeserialized()
    {
        AuthoredStrength  = Strength;
        AuthoredSpeed     = Speed;
        AuthoredDirection = Direction;
    }
}
