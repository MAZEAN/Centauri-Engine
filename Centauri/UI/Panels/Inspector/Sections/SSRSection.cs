namespace Centauri.UI.Panels.Inspector.Sections;

using Config;
using World;
using Common;

public sealed class SSRSection : ISection
{
    private readonly AppConfig _config;

    public SSRSection(AppConfig config) => _config = config;

    public void Draw(Scene scene)
    {
        using var s = Widgets.Section("Screen-Space Reflections", startCollapsed: true);
        if (!s.Open) return;

        var conf = _config.SSR;

        Widgets.CheckRow("Enabled", conf.Enabled, v => conf.Enabled = v);

        Widgets.DragRow("Intensity",    conf.Intensity,    v => conf.Intensity    = v,
            0.01f, 0f, 4f, "%.2f", conf.AuthoredIntensity);
        Widgets.DragRow("Max Distance", conf.MaxDistance,   v => conf.MaxDistance  = v,
            0.1f, 1f, 100f, "%.1f", conf.AuthoredMaxDistance);
        Widgets.DragRow("Thickness",    conf.Thickness,    v => conf.Thickness    = v,
            0.01f, 0.01f, 10f, "%.2f", conf.AuthoredThickness);
        Widgets.DragRow("Max Steps",    conf.MaxSteps,
            v => conf.MaxSteps = Math.Clamp((int)MathF.Round(v), 4, 256),
            1f, 4f, 256f, "%.0f", conf.AuthoredMaxSteps);
        Widgets.DragRow("Refine Steps", conf.RefineSteps,
            v => conf.RefineSteps = Math.Clamp((int)MathF.Round(v), 0, 16),
            1f, 0f, 16f, "%.0f", conf.AuthoredRefineSteps);
        Widgets.DragRow("Roughness Cutoff", conf.RoughnessCutoff, v => conf.RoughnessCutoff = v,
            0.01f, 0f, 1f, "%.2f", conf.AuthoredRoughnessCutoff);
    }
}