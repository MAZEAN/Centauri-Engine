namespace Centauri.UI.Panels.Inspector.Sections;

using Config;
using World;
using Common;

public sealed class TAASection : ISection
{
    private readonly TAAConfig _config;

    public TAASection(TAAConfig config) => _config = config;

    public void Draw(Scene scene)
    {
        using var s = Widgets.Section("Temporal AA", startCollapsed: true);
        if (!s.Open) return;

        Widgets.CheckRow("Enabled", _config.Enabled, v => _config.Enabled = v);

        Widgets.DragRow("Feedback", _config.Feedback, v => _config.Feedback = v,
            0.005f, 0.5f, 0.98f, "%.3f", _config.AuthoredFeedback);
    }
}