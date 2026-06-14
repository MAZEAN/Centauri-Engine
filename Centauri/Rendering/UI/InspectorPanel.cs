namespace Centauri.Rendering.UI;

using ImGuiNET;
using System.Numerics;

using World;

public class InspectorPanel
{
    private const float Width   = 300f;
    private const float Padding = 10f;
    private const float BgAlpha = 0.85f;

    private static readonly string[] LightTypes = ["None", "Directional", "Point", "Spot"];

    private readonly ImFontPtr _font;

    private Entity? _tracked;
    private Vector3 _euler;   // cached working rotation (deg) for the selected entity
    
    private const ImGuiWindowFlags Flags = ImGuiWindowFlags.NoMove          |
                                           ImGuiWindowFlags.NoCollapse      |
                                           ImGuiWindowFlags.NoSavedSettings |
                                           ImGuiWindowFlags.AlwaysAutoResize;

    public InspectorPanel(ImFontPtr font) => _font = font;

    public void Render(Scene scene)
    {
        SetupWindow();

        if (!ImGui.Begin("Inspector", Flags))
        {
            ImGui.End();
            return;
        }

        ImGui.PushFont(_font);

        if (scene.Selected is not { } entity)
        {
            ImGui.TextDisabled("No entity selected");
        }
        else
        {
            if (!ReferenceEquals(entity, _tracked))   // re-seed euler on selection change
            {
                _tracked = entity;
                _euler   = entity.Transform.EulerAngles;
            }

            GUI.CheckRow("Enabled", entity.Enabled, v => entity.Enabled = v);
            ImGui.Spacing();

            DrawTransform(entity);
            DrawMaterial(entity);
            DrawLight(entity);
        }

        ImGui.PopFont();
        ImGui.End();
    }

    private static void SetupWindow()
    {
        var viewport = ImGui.GetMainViewport();
        var anchor = new Vector2(viewport.WorkPos.X + viewport.WorkSize.X - Padding, viewport.WorkPos.Y + Padding);
        
        ImGui.SetNextWindowPos(anchor, ImGuiCond.Always, new Vector2(1f, 0f)); 
        ImGui.SetNextWindowSizeConstraints(
            new Vector2(Width, 0),
            new Vector2(Width, float.MaxValue));
        ImGui.SetNextWindowBgAlpha(BgAlpha);
    }

        private void DrawTransform(Entity e)
    {
        bool open = GUI.BeginPanel("Transform");
        if (open)
        {
            var t = e.Transform;
            GUI.Vec3Rows("Location", t.Position, v => t.Position = v, 0.05f, "%.3f m");

            if (GUI.Vec3Rows("Rotation", ref _euler, 0.5f, "%.1f°"))   // cached euler (pitch, yaw, roll)
                t.SetEulerAngles(_euler.X, _euler.Y, _euler.Z);

            GUI.Vec3Rows("Scale", t.Scale, v => t.Scale = v, 0.01f, "%.3f");
        }
        GUI.EndPanel(open);
    }

    private static void DrawMaterial(Entity e)
    {
        if (e.Material is not { } mat) return;

        bool open = GUI.BeginPanel("Material");
        if (open)
        {
            GUI.ColorRow4("Base Color", mat.Color, v => mat.Color = v);
            GUI.SliderRow("Roughness", mat.RoughnessValue, v => mat.RoughnessValue = v, 0f, 1f); // lower = shinier
            GUI.SliderRow("Metallic",  mat.MetallicValue,  v => mat.MetallicValue  = v, 0f, 1f);
        }
        GUI.EndPanel(open);
    }

    private static void DrawLight(Entity e)
    {
        bool open = GUI.BeginPanel("Light");
        if (!open) { GUI.EndPanel(open); return; }

        var typeIndex = e.Light switch
        {
            DirectionalLight => 1,
            PointLight       => 2,
            SpotLight        => 3,
            _                => 0
        };

        // one control to add / remove / switch the light type
        if (GUI.ComboRow("Type", ref typeIndex, LightTypes))
            e.Light = typeIndex == 0 ? null : CreateLight(typeIndex, e.Light);

        if (e.Light is not { } light) { GUI.EndPanel(open); return; }

        GUI.CheckRow("Light Enabled", light.Enabled, v => light.Enabled = v);
        GUI.ColorRow3("Color##light", light.Color, v => light.Color = v);
        GUI.DragRow("Intensity", light.Intensity, v => light.Intensity = v, 0.05f, 0f, 100f);

        switch (light)
        {
            case DirectionalLight d:
                GUI.Vec3Rows("Direction", d.Direction, v => d.Direction = v, 0.01f, "%.3f");
                break;
            case SpotLight s:
                GUI.Vec3Rows("Direction", s.Direction, v => s.Direction = v, 0.01f, "%.3f");
                GUI.DragRow("Inner Cutoff", s.InnerCutoff, v => s.InnerCutoff = v, 0.5f, 0f, 90f, "%.1f°");
                GUI.DragRow("Outer Cutoff", s.OuterCutoff, v => s.OuterCutoff = v, 0.5f, 0f, 90f, "%.1f°");
                break;
            case PointLight p:
                GUI.DragRow("Linear",    p.Linear,    v => p.Linear    = v, 0.001f, 0f, 1f);
                GUI.DragRow("Quadratic", p.Quadratic, v => p.Quadratic = v, 0.001f, 0f, 1f);
                break;
        }

        GUI.EndPanel(open);
    }

    private static Light CreateLight(int typeIndex, Light? from)
    {
        Light light = typeIndex switch
        {
            1 => new DirectionalLight(),
            2 => new PointLight(),
            3 => new SpotLight(),
            _ => throw new ArgumentOutOfRangeException(nameof(typeIndex))
        };

        if (from is null) return light;

        light.Color     = from.Color;
        light.Intensity = from.Intensity;
        light.Enabled   = from.Enabled;
        return light;
    }
}