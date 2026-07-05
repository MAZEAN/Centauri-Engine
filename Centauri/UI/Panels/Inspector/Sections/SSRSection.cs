namespace Centauri.UI.Panels.Inspector.Sections;

using Config;
using World;
using Common;

public sealed class SSRSection : ISection
{
    private readonly SSRConfig _config;

    public SSRSection(SSRConfig config) => _config = config;

    public void Draw(Scene scene)
    {
        using var s = Widgets.Section("Screen-Space Reflections", startCollapsed: true);
        if (!s.Open) return;
        
        Widgets.CheckRow("Enabled", _config.Enabled, v => _config.Enabled = v);

        Widgets.DragRow("Intensity",    _config.Intensity,    v => _config.Intensity    = v,
            0.01f, 0f, 4f, "%.2f", _config.AuthoredIntensity);
        Widgets.DragRow("Max Distance", _config.MaxDistance,   v => _config.MaxDistance  = v,
            0.1f, 1f, 100f, "%.1f", _config.AuthoredMaxDistance);
        Widgets.DragRow("Thickness",    _config.Thickness,    v => _config.Thickness    = v,
            0.01f, 0.01f, 10f, "%.2f", _config.AuthoredThickness);
        Widgets.DragRow("Max Steps",    _config.MaxSteps,
            v => _config.MaxSteps = Math.Clamp((int)MathF.Round(v), 4, 256),
            1f, 4f, 256f, "%.0f", _config.AuthoredMaxSteps);
        Widgets.DragRow("Refine Steps", _config.RefineSteps,
            v => _config.RefineSteps = Math.Clamp((int)MathF.Round(v), 0, 16),
            1f, 0f, 16f, "%.0f", _config.AuthoredRefineSteps);
        Widgets.DragRow("Roughness Cutoff", _config.RoughnessCutoff, v => _config.RoughnessCutoff = v,
            0.01f, 0f, 1f, "%.2f", _config.AuthoredRoughnessCutoff);
        Widgets.DragRow("Silhouette Threshold", _config.SilhouetteThreshold, v => _config.SilhouetteThreshold = v,
            0.01f, 0.01f, 1f, "%.2f", _config.AuthoredSilhouetteThreshold);
    }
}