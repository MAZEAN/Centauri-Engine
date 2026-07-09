namespace Centauri.UI.Panels.Inspector.Sections;

using ImGuiNET;

using Config;
using World;
using Common;

public sealed class PlanarReflectionSection : ISection
{
    private readonly PlanarReflectionConfig _config;

    public PlanarReflectionSection(PlanarReflectionConfig config) => _config = config;

    public void Draw(Scene scene)
    {
        using var s = Widgets.Section("Planar Reflection", startCollapsed: true);
        if (!s.Open) return;
        
        Widgets.CheckRow("Enabled", _config.Enabled, v => _config.Enabled = v);

        var bound = !string.IsNullOrEmpty(_config.ReflectorEntity);
        if (bound)
            ImGui.TextDisabled($"Plane bound to \"{_config.ReflectorEntity}\"");
        
        Widgets.DragRow(bound ? "Plane Height (fallback)" : "Plane Height",
            _config.PlaneHeight, v => _config.PlaneHeight = v,
            0.05f, -100f, 100f, "%.2f", -1.5f);

        Widgets.DragRow("Intensity", _config.Intensity, v => _config.Intensity = v,
            0.01f, 0f, 4f, "%.2f", 1f);
        
        Widgets.DragRow("Blur", _config.Blur, v => _config.Blur = v,
            0.05f, 0f, 8f, "%.2f", 3f);
        
        Widgets.DragRow("Distortion", _config.Distortion, v => _config.Distortion = v,
            0.001f, 0f, 0.2f, "%.3f", 0f);
    }
}
