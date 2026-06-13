namespace Centauri.Rendering.UI;

using ImGuiNET;
using System.Numerics;

using World;

public class InspectorPanel
{
    private const float Width   = 300f;
    private const float Padding = 10f;

    private readonly ImFontPtr _font;

    private Entity? _tracked;
    private Vector3 _euler;   // working rotation (deg) for the selected entity

    public InspectorPanel(ImFontPtr font) => _font = font;

    public void Render(Scene scene)
    {
        SetupWindow();

        if (!ImGui.Begin("Inspector"))
        {
            ImGui.End();
            return;
        }

        ImGui.PushFont(_font);

        var entity = scene.Selected;
        if (entity is null)
        {
            ImGui.TextDisabled("No entity selected");
        }
        else
        {
            // re-seed the working euler only when the selection changes,
            // so dragging doesn't fight a per-frame quaternion conversion
            if (!ReferenceEquals(entity, _tracked))
            {
                _tracked = entity;
                _euler   = entity.Transform.EulerAngles;
            }

            DrawTransform(entity);
            DrawEntityFlags(entity);
            DrawMaterial(entity);
            DrawLight(entity);
        }

        ImGui.PopFont();
        ImGui.End();
    }

    private static void SetupWindow()
    {
        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(
            new Vector2(vp.WorkPos.X + Padding, vp.WorkPos.Y + Padding),
            ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(Width, 0), ImGuiCond.FirstUseEver);
    }

    private void DrawTransform(Entity e)
    {
        if (!ImGui.CollapsingHeader("Transform", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        var t = e.Transform;

        var pos = t.Position;
        if (ImGui.DragFloat3("Position", ref pos, 0.05f))
            t.Position = pos;

        if (ImGui.DragFloat3("Rotation", ref _euler, 0.5f))   // pitch, yaw, roll
            t.SetEulerAngles(_euler.X, _euler.Y, _euler.Z);

        var scale = t.Scale;
        if (ImGui.DragFloat3("Scale", ref scale, 0.05f))
            t.Scale = scale;
    }

    private static void DrawEntityFlags(Entity e)
    {
        var enabled = e.Enabled;
        if (ImGui.Checkbox("Enabled", ref enabled))
            e.Enabled = enabled;
    }

    private static void DrawMaterial(Entity e)
    {
        if (e.Material is not { } mat) return;
        if (!ImGui.CollapsingHeader("Material", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        var color = mat.Color;
        if (ImGui.ColorEdit4("Color", ref color))
            mat.Color = color;

        var rough = mat.RoughnessValue;
        if (ImGui.SliderFloat("Roughness", ref rough, 0f, 1f))   // lower = shinier
            mat.RoughnessValue = rough;

        var metal = mat.MetallicValue;
        if (ImGui.SliderFloat("Metallic", ref metal, 0f, 1f))
            mat.MetallicValue = metal;
    }

    private static void DrawLight(Entity e)
    {
        if (e.Light is not { } light) return;
        if (!ImGui.CollapsingHeader("Light", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        var enabled = light.Enabled;
        if (ImGui.Checkbox("Light Enabled", ref enabled))
            light.Enabled = enabled;

        var color = light.Color;
        if (ImGui.ColorEdit3("Color##light", ref color))   // ##id avoids clash with material Color
            light.Color = color;

        var intensity = light.Intensity;
        if (ImGui.DragFloat("Intensity", ref intensity, 0.05f, 0f, 100f))
            light.Intensity = intensity;

        switch (light)
        {
            case DirectionalLight d:
            {
                var dir = d.Direction;
                if (ImGui.DragFloat3("Direction", ref dir, 0.01f))
                    d.Direction = dir;
                break;
            }
            case SpotLight s:
            {
                var dir = s.Direction;
                if (ImGui.DragFloat3("Direction", ref dir, 0.01f)) s.Direction = dir;

                var inner = s.InnerCutoff;
                if (ImGui.DragFloat("Inner Cutoff", ref inner, 0.5f, 0f, 90f)) s.InnerCutoff = inner;

                var outer = s.OuterCutoff;
                if (ImGui.DragFloat("Outer Cutoff", ref outer, 0.5f, 0f, 90f)) s.OuterCutoff = outer;
                break;
            }
            case PointLight p:
            {
                var linear = p.Linear;
                if (ImGui.DragFloat("Linear", ref linear, 0.001f, 0f, 1f)) p.Linear = linear;

                var quad = p.Quadratic;
                if (ImGui.DragFloat("Quadratic", ref quad, 0.001f, 0f, 1f)) p.Quadratic = quad;
                break;
            }
        }
    }
}