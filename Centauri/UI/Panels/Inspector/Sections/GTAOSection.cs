namespace Centauri.UI.Panels.Inspector.Sections;

using Config;
using World;
using Common;

public sealed class GTAOSection : ISection
{
    private readonly GTAOConfig _config;

    public GTAOSection(GTAOConfig config) => _config = config;

    public void Draw(Scene scene)
    {
        using var s = Widgets.Section("GTAO", startCollapsed: true);
        if (!s.Open) return;
        
        Widgets.CheckRow("Enabled", _config.Enabled, v => _config.Enabled = v);

        Widgets.DragRow("Radius", _config.Radius, v => _config.Radius = v,
            0.01f, 0.05f, 5f, "%.2f", _config.AuthoredRadius);
        Widgets.DragRow("Power",  _config.Power,  v => _config.Power  = v,
            0.05f, 0.1f, 8f, "%.2f", _config.AuthoredPower);
        Widgets.DragRow("Slices", _config.SliceCount,
            v => _config.SliceCount = Math.Clamp((int)MathF.Round(v), 1, 8),
            1f, 1f, 8f, "%.0f", _config.AuthoredSliceCount);
        Widgets.DragRow("Steps per slice", _config.StepCount,
            v => _config.StepCount = Math.Clamp((int)MathF.Round(v), 1, 16),
            1f, 1f, 16f, "%.0f", _config.AuthoredStepCount);
    }
}