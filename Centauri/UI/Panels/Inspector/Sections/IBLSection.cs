namespace Centauri.UI.Panels.Inspector.Sections;

using Config;
using World;
using Common;

public sealed class IBLSection : ISection
{
    private readonly IBLConfig _config;

    public IBLSection(IBLConfig config) => _config = config;

    public void Draw(Scene scene)
    {
        using var s = Widgets.Section("IBL Config", startCollapsed: true);
        if (!s.Open) return;
        
        Widgets.DragRow("IBLIntensity", _config.IblIntensity, v => _config.IblIntensity = v,
            0.01f, 0f, 2.0f, "%.3f", _config.AuthoredIblIntensity);
    }
}