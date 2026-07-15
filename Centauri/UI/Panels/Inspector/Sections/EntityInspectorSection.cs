namespace Centauri.UI.Panels.Inspector.Sections;

using ImGuiNET;
using System.Numerics;

using World;
using Common;
using Graphics.Resources.Materials;
using Rendering;
using Loading;
using Simulation.Physics;

// The selected-entity inspector: name/enabled header plus the Transform / Material / Light
// sub-panels. Holds the transient rotation-edit state. Shows a placeholder when nothing
// is selected.
public sealed class EntityInspectorSection : ISection
{
    private static readonly string[] LightTypes   = ["None", "Directional", "Point", "Spot"];
    private static readonly string[] PhysicsKinds  = ["None", "Dynamic", "Static"];
    private static readonly string[] PhysicsShapes = ["Box", "Sphere"];

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
        DrawPhysics(entity);
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

        // UvScale/UvOffset only affect texture-sampled shading (fUv in the fragment shader) — a
        // material with no bound texture maps has nothing for them to tile/shift, so dragging
        // these does nothing visible even though it's working correctly. Flag that explicitly
        // instead of leaving it looking broken.
        if (!HasAnyTexture(e))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
            ImGui.TextWrapped("No texture maps bound - UV mapping has no visible effect.");
            ImGui.PopStyleColor();
        }

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

            // The checkbox itself has no "binding" toggle equivalent — unlike Triplanar/Wind it
            // needs a height map bound in the first place (see HasAnyTexture), which the
            // inspector has no binding UI for yet (materials are bound via .mat files only).
            // A live view of the actual offset this produces is the viewport toolbar's
            // "ParallaxDebug" shading mode (or the G cycle hotkey) — global, not per-material,
            // since the effect is subtle-to-invisible at near head-on angles by design and
            // otherwise hard to eyeball as "working" vs. silently not, on whichever material
            // happens to be selected.
            if (mat.Height != null)
            {
                Widgets.CheckRow("Displacement", mat.ParallaxEnabled,
                    v => EditMaterial(e, scene, index, m => m.ParallaxEnabled = v));

                if (mat.ParallaxEnabled)
                    Widgets.DragRow("Parallax Scale", mat.ParallaxScale,
                        v => EditMaterial(e, scene, index, m => m.ParallaxScale = v), 0.005f, 0f, 0.5f, "%.3f", 0.05f);
            }

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
        if (idx >= 0) 
            _selectedMaterial = idx;
    }

    // AO isn't checked here — ResourceSystem.LoadMaterial always assigns it a fallback
    // DefaultTexture when the .mat file doesn't set one, so it's never actually null (unlike
    // the other maps), and would defeat this check for every untextured material.
    private static bool HasAnyTexture(Entity e)
    {
        foreach (var mat in e.Materials)
            if (mat is { Albedo: not null } or { Normal: not null } or { Roughness: not null }
                     or { Metallic: not null } or { Height: not null })
                return true;
        return false;
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
                Widgets.CheckRow("Casts Shadow", sp.CastsShadow, v => sp.CastsShadow = v);
                if (sp.CastsShadow)
                    Widgets.DragRow("Shadow Range", sp.Range, v => sp.Range = v,
                        0.5f, 1f, 200f, "%.1f m", 25f);
                break;
            case PointLight p:
                Widgets.DragRow("Linear",    p.Linear,    v => p.Linear    = v,
                    0.001f, 0f, 1f, "%.3f", 0.09f);
                Widgets.DragRow("Quadratic", p.Quadratic, v => p.Quadratic = v,
                    0.001f, 0f, 1f, "%.3f", 0.032f);
                break;
        }
    }
    
    // Attaches/edits/detaches a RigidBody on the selected entity. Shape is derived from the
    // model's own bounds (see PhysicsSystem.Register) — there's nothing to author there beyond
    // Box/Sphere. Any edit after the initial attach calls RigidBody.MarkDirty() so PhysicsSystem
    // tears down and recreates the BEPU body on its next Sync instead of silently keeping the old
    // one; SyncRigidBodyDefinition mirrors the same edit into the entity's saved definition so it
    // round-trips (see EntitySetLoader.Save — it just re-emits source.Components verbatim).
    private void DrawPhysics(Entity e)
    {
        using var s = Widgets.Section("Physics");
        if (!s.Open) return;

        var rb = e.GetComponent<RigidBody>();
        var kindIndex = rb switch
        {
            null                      => 0,
            { Kind: BodyKind.Static } => 2,
            _                         => 1
        };

        if (Widgets.ComboRow("Body", ref kindIndex, PhysicsKinds))
        {
            if (kindIndex == 0)
            {
                if (rb is not null)
                {
                    e.RemoveComponent<RigidBody>();
                    _entitySetLoader.SyncRigidBodyDefinition(e, null);
                }
                return;
            }

            var kind = kindIndex == 2 ? BodyKind.Static : BodyKind.Dynamic;
            if (rb is null)
            {
                rb = e.AddComponent(new RigidBody { Kind = kind });
            }
            else
            {
                rb.Kind = kind;
                rb.MarkDirty();
            }
            _entitySetLoader.SyncRigidBodyDefinition(e, rb);
        }

        if (rb is null) return;

        var shapeIndex = rb.Shape == BodyShape.Sphere ? 1 : 0;
        if (Widgets.ComboRow("Shape", ref shapeIndex, PhysicsShapes))
        {
            rb.Shape = shapeIndex == 1 ? BodyShape.Sphere : BodyShape.Box;
            rb.MarkDirty();
            _entitySetLoader.SyncRigidBodyDefinition(e, rb);
        }

        if (rb.Kind != BodyKind.Dynamic) return;

        Widgets.DragRow("Mass", rb.Mass, v =>
        {
            rb.Mass = MathF.Max(0.001f, v);
            rb.MarkDirty();
            _entitySetLoader.SyncRigidBodyDefinition(e, rb);
        }, 0.05f, 0.001f, 10000f, "%.3f kg", 1f);

        // Live physical state — read straight off RigidBody, refreshed every fixed step by
        // PhysicsSystem.StepFixed (Vector, then magnitude for a quick at-a-glance read). All zero
        // until physics.enabled and the first Sync() registers the body.
        ImGui.Spacing();
        ImGui.TextDisabled("Live");
        ReadOnlyRow("Velocity",     $"{Widgets.Vec3(rb.LinearVelocity)}  ({Widgets.Float(rb.LinearVelocity.Length())} m/s)");
        ReadOnlyRow("Angular Vel.", $"{Widgets.Vec3(rb.AngularVelocity)}  ({Widgets.Float(rb.AngularVelocity.Length())} rad/s)");
        ReadOnlyRow("Acceleration", $"{Widgets.Vec3(rb.LinearAcceleration)}  ({Widgets.Float(rb.LinearAcceleration.Length())} m/s²)");
    }

    private static void ReadOnlyRow(string label, string value)
    {
        ImGui.TextUnformatted(label);
        ImGui.SameLine();
        ImGui.TextDisabled(value);
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
