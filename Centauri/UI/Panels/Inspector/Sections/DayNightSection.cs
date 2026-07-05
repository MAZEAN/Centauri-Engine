namespace Centauri.UI.Panels.Inspector.Sections;

using ImGuiNET;

using World;
using World.Components;
using Common;

// Controls for the scene's DayNightCycle component, if it has one: scrub straight to a time of
// day, play/pause, and speed the cycle up to preview lighting/shadows across a full day without
// waiting for it to actually pass. No-ops entirely for scenes with no DayNightCycle (e.g. a
// fixed SunOrbit or static sun).
public sealed class DayNightSection : ISection
{
    public void Draw(Scene scene)
    {
        if (scene.FindComponent<DayNightCycle>() is not { } cycle) return;

        using var s = Widgets.Section("Day / Night Cycle", startCollapsed: true);
        if (!s.Open) return;
        
        Widgets.CheckRow("Enabled", !cycle.Paused, v => cycle.Toggle());
        
        if (ImGui.Button("Sunrise"))  
            cycle.SetTimeOfDay(0.25f);
        ImGui.SameLine();
        if (ImGui.Button("Noon"))     
            cycle.SetTimeOfDay(0.5f);
        ImGui.SameLine();
        if (ImGui.Button("Sunset"))   
            cycle.SetTimeOfDay(0.75f);
        ImGui.SameLine();
        if (ImGui.Button("Midnight")) 
            cycle.SetTimeOfDay(0f);

        Widgets.SliderRow("Time of Day (h)", cycle.TimeOfDay * 24f,
            v => cycle.SetTimeOfDay(v / 24f), 0f, 24f, cycle.AuthoredTimeOfDay * 24f);

        Widgets.DragRow("Speed", cycle.SpeedMultiplier, v => cycle.SpeedMultiplier = MathF.Max(0f, v),
            0.05f, 0f, 100f, "%.2fx", cycle.AuthoredSpeed);
    }
}