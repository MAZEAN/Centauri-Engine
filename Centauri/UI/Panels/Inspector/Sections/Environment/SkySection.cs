namespace Centauri.UI.Panels.Inspector.Sections;

using Config;
using World;
using Common;

// Procedural atmosphere controls. When Procedural is on, the skybox shader computes the sky
// from the sun's live direction + turbidity instead of sampling the panorama. Turbidity and
// intensity are best tuned live here against the current sun position/exposure.
public sealed class SkySection : ISection
{
    private readonly SkyConfig _config;

    public SkySection(SkyConfig config) => _config = config;

    public void Draw(Scene scene)
    {
        using var s = Widgets.Section("Procedural Sky", startCollapsed: true);
        if (!s.Open) return;
        
        Widgets.CheckRow("Procedural", _config.Procedural, v => _config.Procedural = v);

        Widgets.DragRow("Turbidity", _config.Turbidity, v => _config.Turbidity = v,
            0.02f, 1f, 10f, "%.2f", _config.AuthoredTurbidity);
        Widgets.DragRow("Intensity", _config.Intensity, v => _config.Intensity = v,
            0.02f, 0f, 8f, "%.2f", _config.AuthoredIntensity);
        Widgets.DragRow("Sun Size (deg)", _config.SunAngularSizeDeg, v => _config.SunAngularSizeDeg = v,
            0.01f, 0.05f, 5f, "%.2f", _config.AuthoredSunAngularSizeDeg);
        Widgets.DragRow("Sun Glow", _config.SunGlowExponent, v => _config.SunGlowExponent = v,
            5f, 50f, 2000f, "%.0f", _config.AuthoredSunGlowExponent);
        
        Widgets.CheckRow("Clouds", _config.Clouds, v => _config.Clouds = v);
        
        Widgets.DragRow("Cloud Coverage", _config.CloudCoverage, v => _config.CloudCoverage = v,
            0.01f, 0f, 1f, "%.2f", _config.AuthoredCloudCoverage);
        Widgets.DragRow("Cloud Scale", _config.CloudScale, v => _config.CloudScale = v,
            0.02f, 0.2f, 10f, "%.2f", _config.AuthoredCloudScale);
        Widgets.DragRow("Cloud Speed", _config.CloudSpeed, v => _config.CloudSpeed = v,
            0.002f, 0f, 0.5f, "%.3f", _config.AuthoredCloudSpeed);
        Widgets.DragRow("Cloud Shading", _config.CloudShading, v => _config.CloudShading = v,
            0.02f, 0f, 3f, "%.2f", _config.AuthoredCloudShading);
    }
}