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
        DrawHierarchy(entity, scene);
        DrawMaterial(entity, scene);
        DrawLight(entity);
        DrawPhysics(entity);
    }

    // Parent picker — the only authoring path for Transform hierarchy this pass (no
    // drag-and-drop reparenting in the Outliner yet; see Docs/Documentation/TransformHierarchy.md
    // "Known limitations"). Rebuilds its option list from the live scene every draw rather than
    // lazily-caching like the model/material pickers elsewhere in this file — unlike those
    // registries, which model exists doesn't change while a scene is open, but which *entities*
    // exist does (add/delete). Excludes the entity itself and every one of its own descendants
    // from the list up front, so an invalid selection simply isn't offered — cheaper for the user
    // than picking one and having EntitySetLoader.SetParent silently refuse it.
    private void DrawHierarchy(Entity entity, Scene scene)
    {
        using var s = Widgets.Section("Hierarchy");
        if (!s.Open) return;

        var names = new List<string> { "(None)" };
        var candidates = new List<Entity?> { null };

        foreach (var other in scene.Entities)
        {
            if (ReferenceEquals(other, entity)) continue;
            if (WouldCreateCycle(other.Transform, entity.Transform)) continue;

            names.Add(other.Name);
            candidates.Add(other);
        }

        var currentParent = entity.Transform.Parent is { } p ? FindOwner(p, scene) : null;
        var index = Math.Max(0, candidates.IndexOf(currentParent));

        if (Widgets.ComboRow("Parent", ref index, names.ToArray()))
            _entitySetLoader.SetParent(entity, candidates[index]);

        if (entity.Transform.Children.Count > 0)
            ImGui.TextDisabled($"{entity.Transform.Children.Count} child(ren)");
    }

    private static Entity? FindOwner(Transform transform, Scene scene)
    {
        foreach (var e in scene.Entities)
            if (ReferenceEquals(e.Transform, transform))
                return e;
        return null;
    }

    // Would entity.Transform.Parent = candidate create a cycle? True when candidate is entity
    // itself or already a descendant of it — mirrors Transform's own private IsAncestorOf check
    // (duplicated here, not exposed, since this is a display-filtering concern, not something
    // Transform's public API needs to answer for its own sake).
    private static bool WouldCreateCycle(Transform candidate, Transform entity)
    {
        for (var current = candidate; current != null; current = current.Parent)
            if (current == entity)
                return true;
        return false;
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

        // Per-slot ids, not lazily cached like the raw registry list below — which *entity* (and
        // therefore which slot currently points at which id) changes with selection, unlike the
        // registry itself. Cheap: a handful of slots, recomputed once per open-section draw.
        var materialIds = _materialIds ??= _resourceSystem.MaterialIds.ToArray();
        var slotIds = _entitySetLoader.GetMaterialIdsPerSlot(e);

        for (var i = 0; i < e.Materials.Count; i++)
        {
            if (e.Materials[i] is not { } mat) continue;
            var index = i;   // capture for the edit closures

            ImGui.PushID(i);

            // One visually distinct sub-header per mesh slot (the mesh's own name from the source
            // file when it has one, e.g. "Bark"/"Leaves" for a tree — falls back to a slot number
            // for code-generated or unnamed meshes) — this, plus the per-slot picker right under
            // it, is what makes a multi-material entity's slots (a tree's bark vs. leaves, say)
            // actually distinguishable and independently editable, rather than one undifferentiated
            // block of property rows with no indication which slot they belong to.
            var meshName = e.Model != null && i < e.Model.Meshes.Count ? e.Model.Meshes[i].Name : "";
            ImGui.PushStyleColor(ImGuiCol.Text, ColorPalette.White);
            ImGui.SeparatorText(string.IsNullOrEmpty(meshName) ? $"Slot {i}" : meshName);
            ImGui.PopStyleColor();

            if (materialIds.Length > 0)
            {
                var selected = slotIds[i] is { } id ? Math.Max(0, Array.IndexOf(materialIds, id)) : 0;
                if (Widgets.ComboRow("Asset", ref selected, materialIds))
                    _entitySetLoader.SetMaterialSlot(e, index, materialIds[selected]);
            }

            // Per-slot, not entity-level — each mesh slot's texture tiles/shifts independently
            // now (Material.UvScale/UvOffset, applied as a per-draw-call uniform — see
            // ShaderUniformBinder.UploadMaterial), matching every other per-slot property below.
            Widgets.Vec2Row("UV Scale",  mat.UvScale,  v => EditMaterial(e, scene, index, m => m.UvScale  = v), 0.01f);
            Widgets.Vec2Row("UV Offset", mat.UvOffset, v => EditMaterial(e, scene, index, m => m.UvOffset = v), 0.01f);

            // UvScale/UvOffset only affect texture-sampled shading (fUv in the fragment shader) —
            // a slot with no bound texture maps has nothing for them to tile/shift, so dragging
            // these does nothing visible even though it's working correctly. Flag that explicitly
            // instead of leaving it looking broken.
            if (!HasAnyTexture(mat))
            {
                ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
                ImGui.TextWrapped("No texture maps bound - UV mapping has no visible effect.");
                ImGui.PopStyleColor();
            }

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

    // AO isn't checked here — ResourceSystem.LoadMaterial always assigns it a fallback
    // DefaultTexture when the .mat file doesn't set one, so it's never actually null (unlike
    // the other maps), and would defeat this check for every untextured material.
    private static bool HasAnyTexture(Material mat) =>
        mat is { Albedo: not null } or { Normal: not null } or { Roughness: not null }
             or { Metallic: not null } or { Height: not null };

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
