namespace Centauri.World.Components;

using System.Numerics;

public sealed class DayNightCycle : Component
{
    private readonly float   _speed;        // time-of-day fraction advanced per second
    private readonly float   _dayIntensity; // peak sun intensity at noon
    private readonly Vector3 _dayColor;     // sun color near noon
    private readonly Vector3 _duskColor;    // sun color near the horizon
    
    public bool Paused { get; private set; } = true;
    
    public float Daylight { get; private set; }
    public float TimeOfDay { get; private set; }
    public float SpeedMultiplier { get; set; } = 1f;
    public float AuthoredTimeOfDay { get; }
    public float AuthoredSpeed { get; }

    public DayNightCycle(float speed = 0.02f, float startTime = 0.3f, 
        float dayIntensity = 4f, Vector3? dayColor = null, Vector3? duskColor  = null)
    {
        _speed        = speed;
        TimeOfDay     = Wrap01(startTime);
        _dayIntensity = dayIntensity;
        _dayColor     = dayColor  ?? new Vector3(1.0f, 0.98f, 0.92f);
        _duskColor    = duskColor ?? new Vector3(1.0f, 0.55f, 0.25f);

        AuthoredSpeed = _speed;
        AuthoredTimeOfDay = TimeOfDay;
    }

    public void Toggle() => Paused = !Paused;
    
    public void SetTimeOfDay(float t)
    {
        TimeOfDay = Wrap01(t);
        Apply();
    }
    
    public static float DaylightOf(Scene scene) =>
        scene.FindComponent<DayNightCycle>() is { } cycle ? cycle.Daylight : 1f;

    public static bool IsDay(Scene scene) => DaylightOf(scene) >= 0.5f;

    protected override void OnAttach() => Apply();

    public override void Update(float dt)
    {
        if (Paused) return;
        
        TimeOfDay = Wrap01(TimeOfDay + _speed * SpeedMultiplier * dt);
        Apply();
    }

    private void Apply()
    {
        if (Owner.Light is not DirectionalLight sun) return;
        
        var a = (TimeOfDay - 0.25f) * MathF.Tau;
        var sunPos = new Vector3(MathF.Cos(a), MathF.Sin(a), 0.15f);
        sun.Direction = Vector3.Normalize(-sunPos);
        
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
