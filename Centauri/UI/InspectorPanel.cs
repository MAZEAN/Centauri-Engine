namespace Centauri.UI;

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
    
    private Vector3 _euler;            // cached working rotation (deg) for the selected entity
    private bool    _editingRotation;  // true while a rotation axis is being dragged

    private const ImGuiWindowFlags Flags = GUI.PanelBase;

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

        DrawInspectorElements(scene);
        DrawSkybox(scene);

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

    private void DrawInspectorElements(Scene scene)
    {
        if (scene.Selected is not { } entity)
        {
            ImGui.TextDisabled("No entity selected");
        }
        else
        {
            DrawHeader(entity);                                      // #4
            GUI.CheckRow("Enabled", entity.Enabled, v => entity.Enabled = v);
            ImGui.Spacing();

            DrawTransform(entity);
            DrawMaterial(entity);
            DrawLight(entity);
        }
    }
    
    private static void DrawHeader(Entity e)
    {
        var name = e.Name;
        
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if (ImGui.InputText("##name", ref name, 64))
            e.Name = name;
        
        ImGui.Spacing();
    }

    private void DrawTransform(Entity e)
    {
        var open = GUI.BeginPanel("Transform");
        if (!open) { GUI.EndPanel(open); return; }
        
        ImGui.PushID("Transform");
        var t = e.Transform;
        var a = e.Authored;

        var posReset   = a?.Position ?? Vector3.Zero;
        var rotReset   = a?.Euler    ?? Vector3.Zero;
        var scaleReset = a?.Scale    ?? Vector3.One;

        GUI.Vec3Rows("Location", t.Position, v => t.Position = v, 0.05f, "%.3f m", posReset);

        if (!_editingRotation) _euler = t.EulerAngles;
        if (GUI.Vec3Rows("Rotation", ref _euler, 0.5f, "%.1f°", rotReset, out _editingRotation))
            t.SetEulerAngles(_euler.X, _euler.Y, _euler.Z);

        GUI.Vec3Rows("Scale", t.Scale, v => t.Scale = v, 0.01f, "%.3f", scaleReset);
        ImGui.PopID();
        
        GUI.EndPanel(open);
    }

    private static void DrawMaterial(Entity e)
    {
        if (e.Material is not { } mat) return;

        var open = GUI.BeginPanel("Material");
        if (!open) { GUI.EndPanel(open); return; }
        
        ImGui.PushID("Material");
        GUI.ColorRow4("Base Color", mat.Color, v => mat.Color = v);
        GUI.SliderRow("Roughness", mat.RoughnessValue, v => mat.RoughnessValue = v, 0f, 1f, 0.5f);
        GUI.SliderRow("Metallic",  mat.MetallicValue,  v => mat.MetallicValue  = v, 0f, 1f, 0.1f);
        ImGui.PopID();
        
        GUI.EndPanel(open);
    }

    private static void DrawLight(Entity e)
    {
        var open = GUI.BeginPanel("Light");
        if (!open) { GUI.EndPanel(open); return; }

        ImGui.PushID("Light");

        var typeIndex = e.Light switch
        {
            DirectionalLight => 1,
            PointLight       => 2,
            SpotLight        => 3,
            _                => 0
        };

        if (GUI.ComboRow("Type", ref typeIndex, LightTypes))
            e.Light = typeIndex == 0 ? null : CreateLight(typeIndex, e.Light);

        if (e.Light is { } light)
        {
            GUI.CheckRow("Light Enabled", light.Enabled, v => light.Enabled = v);
            GUI.ColorRow3("Color", light.Color, v => light.Color = v);            // ## hack no longer needed
            GUI.DragRow("Intensity", light.Intensity, v => light.Intensity = v, 0.05f, 0f, 100f, "%.3f", 1f);

            switch (light)
            {
                case DirectionalLight d:
                    GUI.Vec3Rows("Direction", d.Direction, v => d.Direction = v, 0.01f, "%.3f", new Vector3(0f, -1f, 0f));
                    break;
                case SpotLight s:
                    GUI.Vec3Rows("Direction", s.Direction, v => s.Direction = v, 0.01f, "%.3f", new Vector3(0f, -1f, 0f));
                    GUI.DragRow("Inner Cutoff", s.InnerCutoff, v => s.InnerCutoff = v, 0.5f, 0f, 90f, "%.1f°", 12.5f);
                    GUI.DragRow("Outer Cutoff", s.OuterCutoff, v => s.OuterCutoff = v, 0.5f, 0f, 90f, "%.1f°", 17.5f);
                    break;
                case PointLight p:
                    GUI.DragRow("Linear",    p.Linear,    v => p.Linear    = v, 0.001f, 0f, 1f, "%.3f", 0.09f);
                    GUI.DragRow("Quadratic", p.Quadratic, v => p.Quadratic = v, 0.001f, 0f, 1f, "%.3f", 0.032f);
                    break;
            }
        }

        ImGui.PopID();
        GUI.EndPanel(open);
    }
    
    private static void DrawSkybox(Scene scene)
    {
        if (scene.Skyboxes.Active is not { } sky) return;   // no skybox loaded

        var open = GUI.BeginPanel("Skybox");
        if (!open) { GUI.EndPanel(open); return; }

        ImGui.PushID("Skybox");

        if (sky.Texture.IsHdr)
        {
            GUI.DragRow("Exposure",    sky.Exposure,   v => sky.Exposure   = v, 0.01f,  0f, 16f, "%.2f", sky.AuthoredExposure);
            GUI.DragRow("Black Level", sky.BlackLevel, v => sky.BlackLevel = v, 0.001f, 0f, 1f,  "%.3f", sky.AuthoredBlackLevel);
        }
        else
        {
            ImGui.TextDisabled("LDR skybox — no HDR controls");
        }

        ImGui.PopID();
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