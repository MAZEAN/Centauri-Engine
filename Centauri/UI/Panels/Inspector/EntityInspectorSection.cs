namespace Centauri.UI.Panels.Inspector;

using ImGuiNET;
using System.Numerics;

using World;
using Common;
using Graphics.Resources.Materials;

// The selected-entity inspector: name/enabled header plus the Transform / Material / Light
// sub-panels. Holds the transient rotation-edit state. Shows a placeholder when nothing
// is selected.
public sealed class EntityInspectorSection : IInspectorSection
{
    private static readonly string[] LightTypes = ["None", "Directional", "Point", "Spot"];

    private Vector3 _euler;            // cached working rotation (deg) for the selected entity
    private bool    _editingRotation;  // true while a rotation axis is being dragged

    public void Draw(Scene scene)
    {
        if (scene.Selected is not { } entity)
        {
            ImGui.TextDisabled("No entity selected");
            return;
        }

        DrawHeader(entity);
        Widgets.CheckRow("Enabled", entity.Enabled, v => entity.Enabled = v);
        ImGui.Spacing();

        DrawTransform(entity);
        DrawMaterial(entity, scene);
        DrawLight(entity);
    }

    private void DrawTransform(Entity e)
    {
        using var s = Widgets.Section("Transform");
        if (!s.Open) return;

        var t = e.Transform;
        var a = e.Authored;

        var posReset   = a?.Position ?? Vector3.Zero;
        var rotReset   = a?.Euler    ?? Vector3.Zero;
        var scaleReset = a?.Scale    ?? Vector3.One;

        Widgets.Vec3Rows("Location", t.Position, v => t.Position = v,
            0.05f, "%.3f m", posReset);

        if (!_editingRotation) _euler = t.EulerAngles;

        if (Widgets.Vec3Rows("Rotation", ref _euler, 0.5f, "%.1f°", rotReset, out _editingRotation))
            t.SetEulerAngles(_euler.X, _euler.Y, _euler.Z);

        Widgets.Vec3Rows("Scale", t.Scale, v => t.Scale = v,
            0.01f, "%.3f", scaleReset);
    }

    private static void DrawMaterial(Entity e, Scene scene)
    {
        if (e.Material is not { } mat) return;

        using var s = Widgets.Section("Material");
        if (!s.Open) return;

        Widgets.ColorRow4("Base Color", mat.Color, v => EditMaterial(e, scene, m => m.Color = v));
        Widgets.SliderRow("Roughness", mat.RoughnessValue, v => EditMaterial(e, scene, m => m.RoughnessValue = v), 0f, 1f, 0.5f);
        Widgets.SliderRow("Metallic",  mat.MetallicValue,  v => EditMaterial(e, scene, m => m.MetallicValue  = v), 0f, 1f, 0.1f);
    }

    private static void DrawLight(Entity e)
    {
        using var s = Widgets.Section("Light");
        if (!s.Open) return;

        var typeIndex = e.Light switch
        {
            DirectionalLight => 1,
            PointLight       => 2,
            SpotLight        => 3,
            _                => 0
        };

        if (Widgets.ComboRow("Type", ref typeIndex, LightTypes))
            e.Light = typeIndex == 0 ? null : CreateLight(typeIndex, e.Light);

        if (e.Light is not { } light) return;

        Widgets.CheckRow("Light Enabled", light.Enabled, v => light.Enabled = v);
        Widgets.ColorRow3("Color", light.Color, v => light.Color = v);
        Widgets.DragRow("Intensity", light.Intensity, v => light.Intensity = v,
            0.05f, 0f, 100f, "%.3f", 1f);

        switch (light)
        {
            case DirectionalLight d:
                Widgets.Vec3Rows("Direction", d.Direction, v => d.Direction = v,
                    0.01f, "%.3f", new Vector3(0f, -1f, 0f));
                break;
            case SpotLight sp:
                Widgets.Vec3Rows("Direction", sp.Direction, v => sp.Direction = v,
                    0.01f, "%.3f", new Vector3(0f, -1f, 0f));
                Widgets.DragRow("Inner Cutoff", sp.InnerCutoff, v => sp.InnerCutoff = v,
                    0.5f, 0f, 90f, "%.1f°", 12.5f);
                Widgets.DragRow("Outer Cutoff", sp.OuterCutoff, v => sp.OuterCutoff = v,
                    0.5f, 0f, 90f, "%.1f°", 17.5f);
                break;
            case PointLight p:
                Widgets.DragRow("Linear",    p.Linear,    v => p.Linear    = v,
                    0.001f, 0f, 1f, "%.3f", 0.09f);
                Widgets.DragRow("Quadratic", p.Quadratic, v => p.Quadratic = v,
                    0.001f, 0f, 1f, "%.3f", 0.032f);
                break;
        }
    }
    
    private static void EditMaterial(Entity e, Scene scene, Action<Material> apply)
    {
        if (e.MakeMaterialUnique()) scene.MarkDirty();
        apply(e.Material!);
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

    private static void DrawHeader(Entity e)
    {
        var name = e.Name;

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if (ImGui.InputText("##name", ref name, 64))
            e.Name = name;

        ImGui.Spacing();
    }
}
