namespace Centauri.UI.Panels.Inspector.Sections;

using ImGuiNET;
using System.Numerics;

using World;
using Common;
using Graphics.Resources.Materials;
using Rendering;
using Loading;

// The selected-entity inspector: name/enabled header plus the Transform / Material / Light
// sub-panels. Holds the transient rotation-edit state. Shows a placeholder when nothing
// is selected.
public sealed class EntityInspectorSection : ISection
{
    private static readonly string[] LightTypes = ["None", "Directional", "Point", "Spot"];

    private readonly ResourceSystem _resourceSystem;
    private readonly EntitySetLoader _entitySetLoader;

    private Vector3 _euler;            // cached working rotation (deg) for the selected entity
    private bool    _editingRotation;  // true while a rotation axis is being dragged

    // Lazily built once (the registry doesn't change at runtime) — see HierarchyPanel's
    // identical pattern for the "+ Add" model/material pickers.
    private string[]? _materialIds;
    private int _selectedMaterial;

    public EntityInspectorSection(ResourceSystem resourceSystem, EntitySetLoader entitySetLoader)
    {
        _resourceSystem  = resourceSystem;
        _entitySetLoader = entitySetLoader;
    }

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

        // A per-axis Scale row alone means resizing something uniformly needs the same number
        // typed/dragged three times. Shows X as the reference value (meaningless once the scale
        // is already non-uniform, same as any single-value display of a 3-component state), but
        // dragging it always sets all three axes together.
        Widgets.DragRow("Uniform Scale", t.Scale.X, v => t.Scale = new Vector3(v, v, v),
            0.01f, 0.001f, 1000f, "%.3f", scaleReset.X);
    }

    private void DrawMaterial(Entity e, Scene scene)
    {
        if (e.Materials.Count == 0) return;

        using var s = Widgets.Section("Material");
        if (!s.Open) return;

        DrawMaterialPicker(e);
        Widgets.Vec2Row("UV Scale",  e.UvScale,  v => e.UvScale  = v, 0.01f);
        Widgets.Vec2Row("UV Offset", e.UvOffset, v => e.UvOffset = v, 0.01f);
        ImGui.Spacing();

        for (var i = 0; i < e.Materials.Count; i++)
        {
            if (e.Materials[i] is not { } mat) continue;
            var index = i;   // capture for the edit closures

            ImGui.PushID(i);

            SyncSelectedMaterial(e);
            Widgets.ColorRow4("Base Color", mat.Color, v => EditMaterial(e, scene, index, m => m.Color = v));
            Widgets.SliderRow("Roughness", mat.RoughnessScalar, v => EditMaterial(e, scene, index, m => m.RoughnessScalar = v), 0f, 1f, 0.5f);
            Widgets.SliderRow("Metallic",  mat.MetallicScalar,  v => EditMaterial(e, scene, index, m => m.MetallicScalar  = v), 0f, 1f, 0.1f);
            Widgets.SliderRow("Translucency", mat.Translucency, v => EditMaterial(e, scene, index, m => m.Translucency = v), 0f, 1f, 0f);
            Widgets.CheckRow("Two-Sided", mat.TwoSided, v => EditMaterial(e, scene, index, m => m.TwoSided = v));
            Widgets.CheckRow("Wind",      mat.Wind,     v => EditMaterial(e, scene, index, m => m.Wind     = v));
            Widgets.CheckRow("Triplanar", mat.Triplanar, v => EditMaterial(e, scene, index, m => m.Triplanar = v));
            if (mat.Triplanar)
                Widgets.DragRow("Triplanar Scale", mat.TriplanarScale,
                    v => EditMaterial(e, scene, index, m => m.TriplanarScale = v), 0.05f, 0.01f, 100f, "%.2f m", 1f);

            ImGui.PopID();
        }
    }

    // Reassigns every mesh slot to a different material asset at once — see
    // EntitySetLoader.SetMaterial for why this is uniform rather than per-slot. The per-slot
    // rows below still work afterward, now tweaking whichever material was just applied. Applies
    // immediately on selection (same as the Light Type combo below), not behind a separate
    // confirm step.
    private void DrawMaterialPicker(Entity e)
    {
        var materialIds = _materialIds ??= _resourceSystem.MaterialIds.ToArray();
        if (materialIds.Length == 0) return;

        if (Widgets.ComboRow("Material", ref _selectedMaterial, materialIds))
            _entitySetLoader.SetMaterial(e, materialIds[_selectedMaterial]);
    }

    // Keeps _selectedMaterial pointed at whatever's actually authored on the entity, rather than
    // whatever was last picked in a *different* entity's combo — otherwise switching selection
    // shows index 0 (or the previous entity's index) until the user re-picks something.
    private void SyncSelectedMaterial(Entity e)
    {
        var materialIds = _materialIds ??= _resourceSystem.MaterialIds.ToArray();
        if (_entitySetLoader.GetMaterialId(e) is not { } materialId) return;

        var idx = Array.IndexOf(materialIds, materialId);
        if (idx >= 0) _selectedMaterial = idx;
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
    
    private static void EditMaterial(Entity e, Scene scene, int index, Action<Material> apply)
    {
        if (e.MakeMaterialUnique(index)) 
            scene.MarkDirty();
        apply(e.Materials[index]!);
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
