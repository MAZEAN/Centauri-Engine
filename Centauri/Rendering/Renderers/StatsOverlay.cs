namespace Centauri.Rendering.Renderers;

using ImGuiNET;
using System.Numerics;
using Utils.Misc;

public class StatsOverlay
{
    private bool _visible = false;

    public bool Visible => _visible;
    public void Toggle() => _visible = !_visible;

    public void Render(FrameStats stats)
    {
        if (!_visible) return;

        const float Padding   = 10f;
        const float WindowWidth = 260f;

        var viewport = ImGui.GetMainViewport();
        var pos      = new Vector2(
            viewport.WorkPos.X + viewport.WorkSize.X - WindowWidth - Padding,
            viewport.WorkPos.Y + Padding
        );

        ImGui.SetNextWindowPos(pos, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(WindowWidth, 0), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.75f);

        var flags =
            ImGuiWindowFlags.NoDecoration    |
            ImGuiWindowFlags.NoInputs        |
            ImGuiWindowFlags.NoMove          |
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoBringToFrontOnFocus;

        if (!ImGui.Begin("##stats", flags))
        {
            ImGui.End();
            return;
        }

        // ── Frametiming ───────────────────────────────────────────────────────
        ImGui.TextColored(new Vector4(1f, 0.8f, 0.2f, 1f), "Performance");
        ImGui.Separator();
        ImGui.Text($"FPS        {stats.FPS:F1}");
        ImGui.Text($"Frame Time {stats.FrameTime:F2} ms");

        ImGui.Spacing();

        // ── Culling ───────────────────────────────────────────────────────────
        ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.4f, 1f), "Culling");
        ImGui.Separator();
        ImGui.Text($"Total    {stats.TotalEntities}");

        // color drawn count green if culling is doing useful work
        var drawnColor = stats.CulledEntities > 0
            ? new Vector4(0.4f, 1f, 0.4f, 1f)
            : new Vector4(1f,   1f, 1f,   1f);

        ImGui.TextColored(drawnColor, $"Drawn    {stats.DrawnEntities}");
        ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), $"Culled   {stats.CulledEntities}");

        ImGui.Spacing();

        // ── GPU state ─────────────────────────────────────────────────────────
        ImGui.TextColored(new Vector4(0.4f, 0.7f, 1f, 1f), "Renderer");
        ImGui.Separator();
        ImGui.Text($"Draw Calls     {stats.DrawCalls}");
        ImGui.Text($"Texture Binds  {stats.TextureBinds}");

        var frustumColor = stats.FrustumRebuilt
            ? new Vector4(1f, 0.5f, 0.2f, 1f)  // orange — rebuilt this frame
            : new Vector4(0.5f, 0.5f, 0.5f, 1f); // grey   — skipped

        ImGui.TextColored(frustumColor,
            $"Frustum  {(stats.FrustumRebuilt ? "rebuilt" : "cached")}");

        ImGui.End();
    }
}