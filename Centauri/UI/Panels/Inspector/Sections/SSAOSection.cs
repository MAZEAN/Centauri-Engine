namespace Centauri.UI.Panels.Inspector.Sections;

using Config;
using World;
using Common;

public sealed class SSAOSection : ISection
{
    private readonly SSAOConfig _config;

    public SSAOSection(SSAOConfig config) => _config = config;

    public void Draw(Scene scene)
    {
        using var s = Widgets.Section("SSAO", startCollapsed: true);
        if (!s.Open) return;
        
        Widgets.CheckRow("Enabled", _config.Enabled, v => _config.Enabled = v);

        Widgets.DragRow("Radius", _config.Radius, v => _config.Radius = v,
            0.01f, 0.05f, 5f, "%.2f", _config.AuthoredRadius);
        Widgets.DragRow("Bias",   _config.Bias,   v => _config.Bias   = v,
            0.001f, 0f, 0.2f, "%.3f", _config.AuthoredBias);
        Widgets.DragRow("Power",  _config.Power,  v => _config.Power  = v,
            0.05f, 0.1f, 8f, "%.2f", _config.AuthoredPower);

        Widgets.DragRow("Samples", _config.SampleCount,
            v => _config.SampleCount = Math.Clamp((int)MathF.Round(v), 1, 64),
            1f, 1f, 64f, "%.0f", _config.AuthoredSampleCount);
    }
}