namespace Centauri.UI.Panels.Inspector.Sections;

using ImGuiNET;

using Config;
using World;
using Common;

public sealed class PlanarReflectionSection : ISection
{
    private readonly AppConfig _config;

    public PlanarReflectionSection(AppConfig config) => _config = config;

    public void Draw(Scene scene)
    {
        using var s = Widgets.Section("Planar Reflection", startCollapsed: true);
        if (!s.Open) return;

        var conf = _config.PlanarReflection;

        Widgets.CheckRow("Enabled", conf.Enabled, v => conf.Enabled = v);

        // The reflector's world-space Y — must match the floor's top surface, or the
        // reflection sits at the wrong height / the up-facing mask misses it.
        Widgets.DragRow("Plane Height", conf.PlaneHeight, v => conf.PlaneHeight = v,
            0.05f, -100f, 100f, "%.2f", -1.5f);

        Widgets.DragRow("Intensity", conf.Intensity, v => conf.Intensity = v,
            0.01f, 0f, 4f, "%.2f", 1f);

        // Ripple offset driven by the surface normal — 0 is a perfect mirror; raise it once the
        // water surface writes a perturbed (wave) normal to fake waves.
        Widgets.DragRow("Distortion", conf.Distortion, v => conf.Distortion = v,
            0.001f, 0f, 0.2f, "%.3f", 0f);

        ImGui.TextDisabled("Half-res is a startup/resize setting (config).");
    }
}
