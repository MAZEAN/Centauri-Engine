namespace Centauri.UI.Panels.Inspector.Sections;

using Config;
using World;
using Common;

// Scene-wide wind controls. Drives the per-material foliage sway (materials opt in via the
// `wind` flag); when strength or speed is zero the foliage is static and the shadow pass can
// fall back on its static-cascade cache.
public sealed class WindSection : ISection
{
    private readonly WindConfig _config;

    public WindSection(WindConfig config) => _config = config;

    public void Draw(Scene scene)
    {
        using var s = Widgets.Section("Wind", startCollapsed: true);
        if (!s.Open) return;
        
        Widgets.CheckRow("Enabled", _config.Enabled, v => _config.Enabled = v);

        Widgets.DragRow("Strength", _config.Strength, v => _config.Strength = MathF.Max(0f, v),
            0.005f, 0f, 1f, "%.3f", _config.AuthoredStrength);
        Widgets.DragRow("Speed", _config.Speed, v => _config.Speed = MathF.Max(0f, v),
            0.05f, 0f, 10f, "%.2f", _config.AuthoredSpeed);
        Widgets.SliderRow("Direction", _config.Direction, v => _config.Direction = v,
            0f, 360f, _config.AuthoredDirection);
    }
}