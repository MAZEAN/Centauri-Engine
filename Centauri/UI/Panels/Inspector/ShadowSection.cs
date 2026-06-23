namespace Centauri.UI.Panels.Inspector;

using System.Globalization;

using Config;
using World;
using Common;

public sealed class ShadowSection : IInspectorSection
{
    private static readonly uint[] Sizes = [512, 1024, 2048, 4096, 8192];
    private static readonly string[] SizeLabels =
        Array.ConvertAll(Sizes, x => x.ToString(CultureInfo.InvariantCulture));

    private readonly AppConfig _config;

    public ShadowSection(AppConfig config) => _config = config;

    public void Draw(Scene scene)
    {
        using var s = Widgets.Section("Shadows", startCollapsed: true);
        if (!s.Open) return;

        var conf = _config.Shadows;

        Widgets.CheckRow("Enabled", conf.Enabled, v => conf.Enabled = v);

        Widgets.DragRow("Distance",    conf.Distance,   v => conf.Distance   = v,
            0.5f,    1f, 500f,  "%.1f", conf.AuthoredDistance);
        Widgets.DragRow("Depth Bias",  conf.DepthBias,  v => conf.DepthBias  = v,
            0.0001f, 0f, 0.02f, "%.4f", conf.AuthoredDepthBias);
        Widgets.DragRow("Normal Bias", conf.NormalBias, v => conf.NormalBias = v,
            0.1f,    0f, 10.0f, "%.3f", conf.AuthoredNormalBias);

        Widgets.DragRow("PCF Radius", conf.PcfRadius, v => conf.PcfRadius = (int)MathF.Round(v),
            1f, 0f, 4f, "%.0f", conf.AuthoredPcfRadius);

        var sizeIndex = Array.IndexOf(Sizes, conf.Size);
        if (Widgets.ComboRow("Map Size", ref sizeIndex, SizeLabels))
            conf.Size = sizeIndex >= 0 ? Sizes[sizeIndex] : conf.AuthoredSize;

        Widgets.DragRow("Cascades", conf.CascadeCount,
            v => conf.CascadeCount = Math.Clamp((int)MathF.Round(v), 1, conf.MaxCascades),
            1f, 1f, conf.MaxCascades, "%.0f", conf.AuthoredCascadeCount);
        Widgets.DragRow("Split Blend", conf.SplitLambda, v => conf.SplitLambda = v,
            0.01f, 0f, 1f, "%.2f", conf.AuthoredSplitLambda);

        Widgets.CheckRow("Tint Cascades", conf.DebugCascades, v => conf.DebugCascades = v);
    }
}
