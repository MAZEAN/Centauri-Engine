namespace Centauri.UI.Panels.Inspector.Sections;

using Config;
using World;
using Common;

public sealed class IBLSection : ISection
{
    private readonly AppConfig _config;

    public IBLSection(AppConfig config) => _config = config;

    public void Draw(Scene scene)
    {
        using var s = Widgets.Section("IBL Config", startCollapsed: true);
        if (!s.Open) return;

        var conf = _config.IBLConfig;

        Widgets.DragRow("IBLIntensity", conf.IblIntensity, v => conf.IblIntensity = v,
            0.01f, 0f, 2.0f, "%.3f", conf.AuthoredIblIntensity);
    }
}