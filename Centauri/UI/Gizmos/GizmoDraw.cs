namespace Centauri.UI.Gizmos;

using System.Numerics;
using ImGuiNET;

using Common;

// Rendering for the transform gizmo — everything that touches the ImGui foreground draw list.
// Stateless: it's handed the already-computed screen geometry plus which axis is "active" (hovered
// or being dragged, -1 for none) and draws it. TransformGizmo (interaction/state) decides *what*;
// this decides how it looks. Handle sizes live here; hit-test radii live with the interaction.
internal static class GizmoDraw
{
    private const float LineThickness   = 5f;
    private const float ArrowPixels     = 13f;
    private const float BoxPixels       = 10f;  // scale-mode tip square
    private const float CentreDotPixels = 3.5f;

    private static readonly Vector4 Highlight = new(1f, 0.85f, 0.20f, 1f);
    private static readonly Vector4 CentreDot = new(0.9f, 0.9f, 0.9f, 1f);

    // Translate arrows or scale boxes, per axis, plus the centre dot. `activeAxis` (0..2) is drawn
    // in the highlight colour; -1 means none.
    public static void LinearHandles(Vector2 oScreen, ReadOnlySpan<Vector2> ends, ReadOnlySpan<bool> visible, bool isScale, int activeAxis)
    {
        var dl = ImGui.GetForegroundDrawList();
        for (var i = 0; i < 3; i++)
        {
            if (!visible[i]) continue;

            var color = AxisColorU32(i, i == activeAxis);
            if (isScale) 
                ScaleHandle(dl, oScreen, ends[i], color);
            else         
                Arrow(dl, oScreen, ends[i], color);
        }
        CentreDot_(dl, oScreen);
    }

    // Three world-axis rings, each sampled to a projected polyline (same tessellation the hit-test
    // uses) so the perspective ellipse is drawn correctly rather than faked as a flat circle.
    public static void Rings(Vector3 origin, ReadOnlySpan<Vector3> axes, float worldLen, Matrix4x4 viewProj, Vector2 viewport, Vector2 oScreen, int activeAxis)
    {
        var dl = ImGui.GetForegroundDrawList();
        Span<Vector2> pts = stackalloc Vector2[GizmoMath.RingSegments];

        for (var a = 0; a < 3; a++)
        {
            var (u, v) = GizmoMath.PlaneBasis(axes[a]);
            var count  = 0;
            for (var s = 0; s < GizmoMath.RingSegments; s++)
            {
                var theta = s / (float)GizmoMath.RingSegments * MathF.Tau;
                var world = origin + (u * MathF.Cos(theta) + v * MathF.Sin(theta)) * worldLen;
                
                if (GizmoMath.Project(world, viewProj, viewport, out var p)) 
                    pts[count++] = p;
            }
            if (count < 2) continue;

            var color = AxisColorU32(a, a == activeAxis);
            for (var s = 0; s < count; s++)
                dl.AddLine(pts[s], pts[(s + 1) % count], color, Widgets.Scale(LineThickness * 0.6f));
        }
        CentreDot_(dl, oScreen);
    }

    private static void Arrow(ImDrawListPtr dl, Vector2 from, Vector2 to, uint color)
    {
        var dir = to - from;
        var len = dir.Length();
        
        if (len < 1e-3f) return;
        dir /= len;

        var arrow = Widgets.Scale(ArrowPixels);
        var perp  = new Vector2(-dir.Y, dir.X);
        var baseP = to - dir * arrow;

        dl.AddLine(from, baseP, color, Widgets.Scale(LineThickness));
        dl.AddTriangleFilled(to, baseP + perp * (arrow * 0.4f), baseP - perp * (arrow * 0.4f), color);
    }

    private static void ScaleHandle(ImDrawListPtr dl, Vector2 from, Vector2 to, uint color)
    {
        dl.AddLine(from, to, color, Widgets.Scale(LineThickness));
        
        var h = Widgets.Scale(BoxPixels) * 0.5f;
        dl.AddRectFilled(new Vector2(to.X - h, to.Y - h), new Vector2(to.X + h, to.Y + h), color);
    }

    private static void CentreDot_(ImDrawListPtr dl, Vector2 oScreen) =>
        dl.AddCircleFilled(oScreen, Widgets.Scale(CentreDotPixels), ImGui.GetColorU32(CentreDot));

    public static Vector4 AxisColor(int axis) => axis switch
    {
        0 => new Vector4(0.90f, 0.25f, 0.25f, 1f), // X
        1 => new Vector4(0.40f, 0.85f, 0.40f, 1f), // Y
        _ => new Vector4(0.35f, 0.55f, 0.95f, 1f), // Z
    };

    private static uint AxisColorU32(int axis, bool active) =>
        ImGui.GetColorU32(active ? Highlight : AxisColor(axis));
}
