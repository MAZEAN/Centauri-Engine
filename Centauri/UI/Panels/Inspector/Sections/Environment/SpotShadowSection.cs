namespace Centauri.UI.Panels.Inspector.Sections;

using System.Globalization;

using Config;
using World;
using Common;

// Tunables for spot-light shadow maps (SpotShadowMapper) — per-light casting itself is opted
// into from the Entity Inspector's Light section ("Casts Shadow"/"Shadow Range"), not here; this
// section is only the shared atlas/quality settings. Separate from ShadowSection (the
// directional/CSM sun) since the two passes are otherwise fully independent.
public sealed class SpotShadowSection : ISection
{
    private readonly SpotShadowConfig _config;

    private static readonly uint[] Sizes = [256, 512, 1024, 2048];
    private static readonly string[] SizeLabels =
        Array.ConvertAll(Sizes, x => x.ToString(CultureInfo.InvariantCulture));

    public SpotShadowSection(SpotShadowConfig config) => _config = config;

    public void Draw(Scene scene)
    {
        using var s = Widgets.Section("Spot Shadows", startCollapsed: true);
        if (!s.Open) return;

        Widgets.CheckRow("Enabled", _config.Enabled, v => _config.Enabled = v);

        var sizeIndex = Array.IndexOf(Sizes, _config.Size);
        if (Widgets.ComboRow("Map Size", ref sizeIndex, SizeLabels))
            _config.Size = sizeIndex >= 0 ? Sizes[sizeIndex] : _config.AuthoredSize;

        Widgets.DragRow("Depth Bias", _config.DepthBias, v => _config.DepthBias = v,
            0.0001f, 0f, 0.02f, "%.4f", _config.AuthoredDepthBias);
        Widgets.DragRow("Normal Bias", _config.NormalBias, v => _config.NormalBias = v,
            0.1f, 0f, 10f, "%.3f", _config.AuthoredNormalBias);
        Widgets.DragRow("PCF Radius", _config.PcfRadius, v => _config.PcfRadius = (int)MathF.Round(v),
            1f, 0f, 4f, "%.0f", _config.AuthoredPcfRadius);
    }
}
