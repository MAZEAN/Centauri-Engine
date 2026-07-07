namespace Centauri.UI.Panels.Inspector.Sections;

using ImGuiNET;

using Config;
using World;
using Common;
using Rendering.Profiling;

// Tracy profiler toggle + live connection status. See Docs/TracyProfiler.md for what this
// connects to, and how to build the native client library it needs (not bundled — the section
// just reports whether it found one).
public sealed class TracySection : ISection
{
    private const float LabelWidth = 140f;

    private readonly DebugConfig _config;

    public TracySection(DebugConfig config) => _config = config;

    public void Draw(Scene scene)
    {
        using var s = Widgets.Section("Tracy Profiler", startCollapsed: true);
        if (!s.Open) return;

        if (!Tracy.IsAvailable)
        {
            ImGui.TextWrapped("Native library not found, see Docs/TracyProfiler.md to build it.");
            if (Tracy.LoadError is { } error)
                ImGui.TextWrapped(error);
            return;
        }

        Widgets.CheckRow("Enabled", _config.TracyEnabled, v => _config.TracyEnabled = v);

        var startX = ImGui.GetCursorPosX();
        ImGui.TextUnformatted("Connected");
        ImGui.SameLine();
        ImGui.SetCursorPosX(startX + LabelWidth);

        var connected = Tracy.Connected;
        ImGui.TextColored(Widgets.BooleanColor(connected), connected ? "Yes" : "No");
    }
}