namespace Centauri.UI.Panels.Inspector;

using Config;
using World;
using Common;

public sealed class TAASection : IInspectorSection
{
    private readonly AppConfig _config;

    public TAASection(AppConfig config) => _config = config;

    public void Draw(Scene scene)
    {
        using var s = Widgets.Section("Temporal AA", startCollapsed: true);
        if (!s.Open) return;

        var conf = _config.TAA;

        Widgets.CheckRow("Enabled", conf.Enabled, v => conf.Enabled = v);

        Widgets.DragRow("Feedback", conf.Feedback, v => conf.Feedback = v,
            0.005f, 0.5f, 0.98f, "%.3f", conf.AuthoredFeedback);
    }
}