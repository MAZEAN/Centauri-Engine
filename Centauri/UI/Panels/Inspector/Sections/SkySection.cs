namespace Centauri.UI.Panels.Inspector.Sections;

using Config;
using World;
using Common;

// Procedural atmosphere controls. When Procedural is on, the skybox shader computes the sky
// from the sun's live direction + turbidity instead of sampling the panorama. Turbidity and
// intensity are best tuned live here against the current sun position/exposure.
public sealed class SkySection : ISection
{
    private readonly AppConfig _config;

    public SkySection(AppConfig config) => _config = config;

    public void Draw(Scene scene)
    {
        using var s = Widgets.Section("Procedural Sky", startCollapsed: true);
        if (!s.Open) return;

        var conf = _config.Sky;

        Widgets.CheckRow("Procedural", conf.Procedural, v => conf.Procedural = v);

        Widgets.DragRow("Turbidity", conf.Turbidity, v => conf.Turbidity = v,
            0.02f, 1f, 10f, "%.2f", conf.AuthoredTurbidity);
        Widgets.DragRow("Intensity", conf.Intensity, v => conf.Intensity = v,
            0.02f, 0f, 8f, "%.2f", conf.AuthoredIntensity);
        Widgets.DragRow("Sun Size (deg)", conf.SunAngularSizeDeg, v => conf.SunAngularSizeDeg = v,
            0.01f, 0.05f, 5f, "%.2f", conf.AuthoredSunAngularSizeDeg);
        Widgets.DragRow("Sun Glow", conf.SunGlowExponent, v => conf.SunGlowExponent = v,
            5f, 50f, 2000f, "%.0f", conf.AuthoredSunGlowExponent);
    }
}