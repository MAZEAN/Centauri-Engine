namespace Centauri.Rendering.Renderers;

using ImGuiNET;
using System.Numerics;

using Utils.Misc;
using World;

public class StatsOverlay
{
    private readonly ImFontPtr _font;
    public bool IsVisible { get; private set; }

    public void Toggle() => IsVisible = !IsVisible;
    
    private const ImGuiWindowFlags Flags = ImGuiWindowFlags.NoDecoration          |
                                           ImGuiWindowFlags.NoMove                |
                                           ImGuiWindowFlags.NoSavedSettings       |
                                           ImGuiWindowFlags.NoBringToFrontOnFocus |
                                           ImGuiWindowFlags.AlwaysAutoResize;
    
    // Colors
    private readonly Vector4 _defaultColor = new (1.0f, 1.0f, 1.0f, 1.0f);
    
    private readonly Vector4 _orangeColor = new (1.0f, 0.8f, 0.2f, 1.0f);
    private readonly Vector4 _greenColor  = new (0.4f, 0.9f, 0.4f, 1.0f);
    private readonly Vector4 _blueColor   = new (0.4f, 0.7f, 1.0f, 1.0f);
    private readonly Vector4 _redColor    = new (1.0f, 0.4f, 0.4f, 1.0f);

    public StatsOverlay(ImFontPtr font)
    {
        _font = font;
    }
    
    public void Render(Scene scene, FrameStats stats)
    {
        if (!IsVisible)
            return;

        SetupWindow();

        if (!ImGui.Begin("##StatsOverlay", Flags))
        {
            Console.WriteLine("ImGui couldn't start");
            ImGui.End();
            return;
        }

        var cam = scene.GetActiveCamera();
        
        ImGui.PushFont(_font);
        DrawSection("Performance", _orangeColor, () =>
        {
            Row("FPS", $"{stats.FPS:F1}");
            Row("Frame Time", $"{stats.FrameTime:F2} ms");
        });

        DrawSection("Culling", _greenColor, () =>
        {
            Row("Total", stats.TotalEntities.ToString());

            var drawnColor = stats.CulledEntities > 0
                ? _greenColor
                : _defaultColor;

            RowColored("Drawn", stats.DrawnEntities.ToString(), drawnColor);
            RowColored("Culled", stats.CulledEntities.ToString(), _redColor);
            
            var ratio = stats.TotalEntities > 0
                ? (stats.CulledEntities / (float)stats.TotalEntities) * 100f 
                : 0f;

            RowColored("Ratio", $"{ratio:F2}%", _defaultColor);
        });

        DrawSection("Renderer", _blueColor, () =>
        {
            Row("Draw Calls", stats.DrawCalls.ToString());
            Row("Texture Binds", stats.TextureBinds.ToString());
        });

        DrawSection("Camera", _redColor, () =>
        {
            RowColored("Active", cam.Name, _orangeColor);
            RowColored("Position", FormatVec3(cam.Position), _defaultColor);
            RowColored("Forward", FormatVec3(cam.Forward),  _defaultColor);
        });
        ImGui.PopFont();

        ImGui.End();
    }

    // ─────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────

    private void SetupWindow()
    {
        const float padding = 10f;

        var viewport = ImGui.GetMainViewport();
        var anchor = new Vector2(
            viewport.WorkPos.X + viewport.WorkSize.X - padding,
            viewport.WorkPos.Y + padding);

        ImGui.SetNextWindowPos(anchor, ImGuiCond.Always, new Vector2(1f, 0f)); // pivot = top-right
        ImGui.SetNextWindowBgAlpha(0.85f);
    }

    private void DrawSection(string title, Vector4 color, Action content)
    {
        ImGui.TextColored(color, title);
        ImGui.Separator();

        if (ImGui.BeginTable($"##{title}_table", 2, ImGuiTableFlags.SizingFixedFit))
        {
            ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed, 120f);
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);

            content();

            ImGui.EndTable();
        }

        ImGui.Spacing();
    }

    private void Row(string label, string value)
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.TextUnformatted(label);

        ImGui.TableSetColumnIndex(1);
        ImGui.TextUnformatted(value);
    }

    private void RowColored(string label, string value, Vector4 color)
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.TextUnformatted(label);

        ImGui.TableSetColumnIndex(1);
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted(value);
        ImGui.PopStyleColor();
    }

    private string FormatVec3(Vector3 v)
    {
        return $"({v.X:F2}, {v.Y:F2}, {v.Z:F2})";
    }
}