namespace Centauri.UI.Panels.Inspector.Sections;

using Config;
using World;
using Common;

// Scene-wide wind controls. Drives the per-material foliage sway (materials opt in via the
// `wind` flag); when strength or speed is zero the foliage is static and the shadow pass can
// fall back on its static-cascade cache.
public sealed class WindSection : ISection
{
    private readonly AppConfig _config;

    public WindSection(AppConfig config) => _config = config;

    public void Draw(Scene scene)
    {
        using var s = Widgets.Section("Wind", startCollapsed: true);
        if (!s.Open) return;

        var conf = _config.Wind;

        Widgets.CheckRow("Enabled", conf.Enabled, v => conf.Enabled = v);

        Widgets.DragRow("Strength", conf.Strength, v => conf.Strength = MathF.Max(0f, v),
            0.005f, 0f, 1f, "%.3f", conf.AuthoredStrength);
        Widgets.DragRow("Speed", conf.Speed, v => conf.Speed = MathF.Max(0f, v),
            0.05f, 0f, 10f, "%.2f", conf.AuthoredSpeed);
        Widgets.SliderRow("Direction", conf.Direction, v => conf.Direction = v,
            0f, 360f, conf.AuthoredDirection);
    }
}