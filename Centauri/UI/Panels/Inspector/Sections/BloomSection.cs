namespace Centauri.UI.Panels.Inspector.Sections;

using Config;
using World;
using Common;

public sealed class BloomSection : ISection
{
    private readonly AppConfig _config;

    public BloomSection(AppConfig config) => _config = config;

    public void Draw(Scene scene)
    {
        using var s = Widgets.Section("Bloom", startCollapsed: true);
        if (!s.Open) return;

        var conf = _config.Bloom;

        Widgets.CheckRow("Enabled", conf.Enabled, v => conf.Enabled = v);

        Widgets.DragRow("Threshold", conf.Threshold, v => conf.Threshold = v,
            0.01f, 0f, 10f, "%.2f", conf.AuthoredThreshold);
        Widgets.DragRow("Knee",      conf.Knee,      v => conf.Knee      = v,
            0.01f, 0f, 2f, "%.2f", conf.AuthoredKnee);
        Widgets.DragRow("Intensity", conf.Intensity, v => conf.Intensity = v,
            0.01f, 0f, 4f, "%.2f", conf.AuthoredIntensity);
        Widgets.DragRow("Radius",    conf.Radius,    v => conf.Radius    = v,
            0.01f, 0.1f, 4f, "%.2f", conf.AuthoredRadius);
    }
}