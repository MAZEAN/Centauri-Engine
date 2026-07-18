namespace Centauri.UI.Panels.Inspector.Sections;

using Config;
using World;
using Common;

// Eye adaptation controls. When enabled, the tonemap pass derives its own exposure multiplier
// from the scene's measured average brightness, on top of (not instead of) ColorGrading's
// manual Exposure dial — that one still works as an EV-compensation offset either way.
public sealed class AutoExposureSection : ISection
{
    private readonly AutoExposureConfig _config;

    public AutoExposureSection(AutoExposureConfig config) => _config = config;

    public void Draw(Scene scene)
    {
        using var s = Widgets.Section("Auto Exposure", startCollapsed: true);
        if (!s.Open) return;

        var conf = _config;

        Widgets.CheckRow("Enabled", conf.Enabled, v => conf.Enabled = v);

        Widgets.DragRow("Key Value", conf.KeyValue, v => conf.KeyValue = v,
            0.005f, 0.01f, 1f, "%.3f", conf.AuthoredKeyValue);
        Widgets.DragRow("Adapt Speed", conf.AdaptSpeed, v => conf.AdaptSpeed = v,
            0.02f, 0.05f, 10f, "%.2f", conf.AuthoredAdaptSpeed);
        Widgets.DragRow("Min Exposure", conf.MinExposure, v => conf.MinExposure = v,
            0.01f, 0.01f, 4f, "%.2f", conf.AuthoredMinExposure);
        Widgets.DragRow("Max Exposure", conf.MaxExposure, v => conf.MaxExposure = v,
            0.05f, 0.5f, 32f, "%.2f", conf.AuthoredMaxExposure);
    }
}