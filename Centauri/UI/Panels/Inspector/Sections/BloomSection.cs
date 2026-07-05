namespace Centauri.UI.Panels.Inspector.Sections;

using Config;
using World;
using Common;

public sealed class BloomSection : ISection
{
    private readonly BloomConfig _config;

    public BloomSection(BloomConfig config) => _config = config;

    public void Draw(Scene scene)
    {
        using var s = Widgets.Section("Bloom", startCollapsed: true);
        if (!s.Open) return;
        
        Widgets.CheckRow("Enabled", _config.Enabled, v => _config.Enabled = v);

        Widgets.DragRow("Threshold", _config.Threshold, v => _config.Threshold = v,
            0.01f, 0f, 10f, "%.2f", _config.AuthoredThreshold);
        Widgets.DragRow("Knee",      _config.Knee,      v => _config.Knee      = v,
            0.01f, 0f, 2f, "%.2f", _config.AuthoredKnee);
        Widgets.DragRow("Intensity", _config.Intensity, v => _config.Intensity = v,
            0.01f, 0f, 4f, "%.2f", _config.AuthoredIntensity);
        Widgets.DragRow("Radius",    _config.Radius,    v => _config.Radius    = v,
            0.01f, 0.1f, 4f, "%.2f", _config.AuthoredRadius);
    }
}