namespace Centauri.UI.Panels.Inspector;

using ImGuiNET;

using World;
using Common;

public sealed class SkyboxSection : IInspectorSection
{
    public void Draw(Scene scene)
    {
        if (scene.Skyboxes.Active is not { } sky) return;   // no skybox loaded

        using var s = Widgets.Section("Skybox", startCollapsed: true);
        if (!s.Open) return;

        if (sky.Texture.IsHdr)
        {
            Widgets.DragRow("Exposure",    sky.Exposure,   v => sky.Exposure   = v,
                0.01f,  0f, 16f,  "%.2f", sky.AuthoredExposure);
            Widgets.DragRow("Black Level", sky.BlackLevel, v => sky.BlackLevel = v,
                0.001f, 0f, 0.5f, "%.3f", sky.AuthoredBlackLevel);
        }
        else
        {
            ImGui.TextDisabled("LDR skybox — no HDR controls");
        }
    }
}