namespace Centauri.UI.Panels.Inspector.Sections;

using Config;
using World;
using Common;

// Foliage-wide controls: wind sway (materials opt in via the per-material `wind` flag; when
// strength or speed is zero the foliage is static and the shadow pass can fall back on its
// static-cascade cache) and the alpha-tested cutout shared by the lit pass and ZPrepass.
public sealed class FoliageSection : ISection
{
    private readonly FoliageConfig _config;

    public FoliageSection(FoliageConfig config) => _config = config;

    public void Draw(Scene scene)
    {
        using var s = Widgets.Section("Foliage", startCollapsed: true);
        if (!s.Open) return;

        Widgets.CheckRow("Wind Enabled", _config.WindEnabled, v => _config.WindEnabled = v);

        Widgets.DragRow("Wind Strength", _config.WindStrength, v => _config.WindStrength = MathF.Max(0f, v),
            0.005f, 0f, 1f, "%.3f", _config.AuthoredWindStrength);
        Widgets.DragRow("Wind Speed", _config.WindSpeed, v => _config.WindSpeed = MathF.Max(0f, v),
            0.05f, 0f, 10f, "%.2f", _config.AuthoredWindSpeed);
        Widgets.SliderRow("Wind Direction", _config.WindDirection, v => _config.WindDirection = v,
            0f, 360f, _config.AuthoredWindDirection);

        // Lower shows more of the texture's soft alpha edge (softer silhouette, wider band for
        // alpha-to-coverage to dither, but more visible edge fringing on textures with
        // non-premultiplied edge color bleed); higher gives a harder, blockier cutout. Must stay
        // in sync with ZPrepass's matching threshold — see FoliageConfig.AlphaCutoff.
        Widgets.DragRow("Alpha Cutoff", _config.AlphaCutoff, v => _config.AlphaCutoff = Math.Clamp(v, 0f, 1f),
            0.01f, 0f, 1f, "%.2f", _config.AuthoredAlphaCutoff);
    }
}
