namespace Centauri.UI.Panels.Inspector.Sections;

using System.Globalization;

using Config;
using World;
using Common;

public sealed class ShadowSection : ISection
{
    private readonly ShadowConfig _config;
    
    private static readonly uint[] Sizes = [512, 1024, 2048, 4096, 8192];
    private static readonly string[] SizeLabels =
        Array.ConvertAll(Sizes, x => x.ToString(CultureInfo.InvariantCulture));

    public ShadowSection(ShadowConfig config) => _config = config;

    public void Draw(Scene scene)
    {
        using var s = Widgets.Section("Shadows", startCollapsed: true);
        if (!s.Open) return;
        
        Widgets.CheckRow("Enabled", _config.Enabled, v => _config.Enabled = v);

        Widgets.DragRow("Distance",    _config.Distance,   v => _config.Distance   = v,
            0.5f,    1f, 500f,  "%.1f", _config.AuthoredDistance);
        Widgets.DragRow("Depth Bias",  _config.DepthBias,  v => _config.DepthBias  = v,
            0.0001f, 0f, 0.02f, "%.4f", _config.AuthoredDepthBias);
        Widgets.DragRow("Normal Bias", _config.NormalBias, v => _config.NormalBias = v,
            0.1f,    0f, 10.0f, "%.3f", _config.AuthoredNormalBias);
        Widgets.DragRow("PCF Radius", _config.PcfRadius, v => _config.PcfRadius = (int)MathF.Round(v),
            1f, 0f, 4f, "%.0f", _config.AuthoredPcfRadius);
        
        var sizeIndex = Array.IndexOf(Sizes, _config.Size);
        if (Widgets.ComboRow("Map Size", ref sizeIndex, SizeLabels))
            _config.Size = sizeIndex >= 0 ? Sizes[sizeIndex] : _config.AuthoredSize;

        Widgets.DragRow("Cascades", _config.CascadeCount,
            v => _config.CascadeCount = Math.Clamp((int)MathF.Round(v), 1, _config.MaxCascades),
            1f, 1f, _config.MaxCascades, "%.0f", _config.AuthoredCascadeCount);
        Widgets.DragRow("Split Blend", _config.SplitLambda, v => _config.SplitLambda = v,
            0.01f, 0f, 1f, "%.2f", _config.AuthoredSplitLambda);
        
        Widgets.DragRow("Wind Throttle", _config.WindThrottleMs, v => _config.WindThrottleMs = v,
            5f, 0f, 250f, "%.0f", _config.AuthoredWindThrottleMs);
        
        Widgets.CheckRow("Contact Hardening", _config.ContactHardening, v => _config.ContactHardening = v);
        Widgets.DragRow("Light Size", _config.LightSize, v => _config.LightSize = v,
            0.001f, 0f, 0.1f, "%.3f", _config.AuthoredLightSize);
        Widgets.DragRow("Blocker Search", _config.BlockerSearchRadius, v => _config.BlockerSearchRadius = v,
            0.5f, 1f, 16f, "%.1f", _config.AuthoredBlockerSearchRadius);
        Widgets.DragRow("Max Penumbra", _config.MaxPenumbraRadius, v => _config.MaxPenumbraRadius = v,
            1f, 1f, 48f, "%.0f", _config.AuthoredMaxPenumbraRadius);
        
        Widgets.CheckRow("Tint Cascades", _config.DebugCascades, v => _config.DebugCascades = v);
    }
}
