namespace Centauri.UI.Panels.Inspector;

using Config;
using World;
using Common;

public sealed class SSAOSection : IInspectorSection
{
    private readonly AppConfig _config;

    public SSAOSection(AppConfig config) => _config = config;

    public void Draw(Scene scene)
    {
        using var s = Widgets.Section("SSAO", startCollapsed: true);
        if (!s.Open) return;

        var conf = _config.SSAO;

        Widgets.CheckRow("Enabled", conf.Enabled, v => conf.Enabled = v);

        Widgets.DragRow("Radius", conf.Radius, v => conf.Radius = v,
            0.01f, 0.05f, 5f, "%.2f", conf.AuthoredRadius);
        Widgets.DragRow("Bias",   conf.Bias,   v => conf.Bias   = v,
            0.001f, 0f, 0.2f, "%.3f", conf.AuthoredBias);
        Widgets.DragRow("Power",  conf.Power,  v => conf.Power  = v,
            0.05f, 0.1f, 8f, "%.2f", conf.AuthoredPower);

        Widgets.DragRow("Samples", conf.SampleCount,
            v => conf.SampleCount = Math.Clamp((int)MathF.Round(v), 1, 64),
            1f, 1f, 64f, "%.0f", conf.AuthoredSampleCount);
    }
}