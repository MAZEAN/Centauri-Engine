namespace Centauri.World.Components;

using System.Numerics;

// Drives a directional light through a day/night cycle: advances a normalized
// time-of-day, orbiting the sun across an east->overhead->west arc and fading its
// intensity and color from warm dusk to bright noon. Below the horizon the light
// fades to zero (night). Toggle() pauses/resumes — the sun freezes where it is.
public sealed class DayNightCycle : Component
{
    private readonly float   _speed;        // time-of-day fraction advanced per second
    private float _time;                    // [0,1): 0 midnight, .25 sunrise, .5 noon, .75 sunset
    private readonly float   _dayIntensity; // peak sun intensity at noon
    private readonly Vector3 _dayColor;     // sun color near noon
    private readonly Vector3 _duskColor;    // sun color near the horizon
    
    public bool Paused { get; private set; } = true;
    
    // 0 = full night, 1 = full day — drives ambient/IBL dimming and skybox selection
    public float Daylight { get; private set; }

    public DayNightCycle(float speed = 0.02f, float startTime = 0.3f, 
        float dayIntensity = 4f, Vector3? dayColor = null, Vector3? duskColor  = null)
    {
        _speed        = speed;
        _time         = Wrap01(startTime);
        _dayIntensity = dayIntensity;
        _dayColor     = dayColor  ?? new Vector3(1.0f, 0.98f, 0.92f);
        _duskColor    = duskColor ?? new Vector3(1.0f, 0.55f, 0.25f);
    }

    public void Toggle() => Paused = !Paused;
    
    public static float DaylightOf(Scene scene) =>
        scene.FindComponent<DayNightCycle>() is { } cycle ? cycle.Daylight : 1f;

    public static bool IsDay(Scene scene) => DaylightOf(scene) >= 0.5f;

    protected override void OnAttach() => Apply();

    public override void Update(float dt)
    {
        if (Paused) return;
        _time = Wrap01(_time + _speed * dt);
        Apply();
    }

    private void Apply()
    {
        if (Owner.Light is not DirectionalLight sun) return;

        // sun position on an east->overhead->west arc; angle is 0 at sunrise (east horizon)
        var a      = (_time - 0.25f) * MathF.Tau;
        var sunPos = new Vector3(MathF.Cos(a), MathF.Sin(a), 0.15f); // slight north tilt
        sun.Direction = Vector3.Normalize(-sunPos);                  // light travels toward the scene

        // sunPos.Y is elevation in [-1,1]; daylight only once it clears the horizon
        var day = Smoothstep(-0.05f, 0.25f, sunPos.Y);
        Daylight = day;

        sun.Intensity = _dayIntensity * day;
        sun.Color     = Vector3.Lerp(_duskColor, _dayColor, day);
    }

    private static float Wrap01(float v) => v - MathF.Floor(v);

    private static float Smoothstep(float edge0, float edge1, float x)
    {
        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
