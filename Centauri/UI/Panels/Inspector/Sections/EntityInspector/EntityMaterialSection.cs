namespace Centauri.UI.Panels.Inspector.Sections;

using ImGuiNET;
using System.Numerics;

using World;
using Common;
using Graphics.Resources.Materials;
using Rendering;
using Loading;
using Editing.Undo;

internal sealed class EntityMaterialSection
{
    private readonly ResourceSystem _resourceSystem;
    private readonly EntitySetLoader _entitySetLoader;
    private readonly CommandHistory _commandHistory;

    // Lazily built once (the registry doesn't change at runtime) — see HierarchyPanel's
    // identical pattern for the "+ Add" model/material pickers.
    private string[]? _materialIds;

    public EntityMaterialSection(ResourceSystem resourceSystem, EntitySetLoader entitySetLoader, CommandHistory commandHistory)
    {
        _resourceSystem  = resourceSystem;
        _entitySetLoader = entitySetLoader;
        _commandHistory  = commandHistory;
    }

    public void Draw(Entity e, Scene scene)
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
            Widgets.Vec2Row("UV Scale",  mat.UvScale,  v => EditMaterial(e, scene, index, m => m.UvScale  = v),
                0.01f, "%.3f", Vector2.One, _commandHistory);
            Widgets.Vec2Row("UV Offset", mat.UvOffset, v => EditMaterial(e, scene, index, m => m.UvOffset = v),
                0.01f, "%.3f", Vector2.Zero, _commandHistory);

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

            Widgets.ColorRow4("Base Color", mat.Color, v => EditMaterial(e, scene, index, m => m.Color = v), _commandHistory);
            Widgets.SliderRow("Roughness", mat.RoughnessScalar, v => EditMaterial(e, scene, index, m => m.RoughnessScalar = v), 0f, 1f, 0.5f, _commandHistory);
            Widgets.SliderRow("Metallic",  mat.MetallicScalar,  v => EditMaterial(e, scene, index, m => m.MetallicScalar  = v), 0f, 1f, 0.1f, _commandHistory);
            Widgets.SliderRow("Translucency", mat.Translucency, v => EditMaterial(e, scene, index, m => m.Translucency = v), 0f, 1f, 0f, _commandHistory);
            Widgets.CheckRow("Two-Sided", mat.TwoSided, v => EditMaterial(e, scene, index, m => m.TwoSided = v), _commandHistory);
            Widgets.CheckRow("Wind",      mat.Wind,     v => EditMaterial(e, scene, index, m => m.Wind     = v), _commandHistory);
            Widgets.CheckRow("Triplanar", mat.Triplanar, v => EditMaterial(e, scene, index, m => m.Triplanar = v), _commandHistory);
            if (mat.Triplanar)
                Widgets.DragRow("Triplanar Scale", mat.TriplanarScale,
                    v => EditMaterial(e, scene, index, m => m.TriplanarScale = v), 0.05f, 0.01f, 100f, "%.2f m", 1f, _commandHistory);

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
                    v => EditMaterial(e, scene, index, m => m.ParallaxEnabled = v),
                    _commandHistory);

                if (mat.ParallaxEnabled)
                    Widgets.DragRow("Parallax Scale", mat.ParallaxScale,
                        v => EditMaterial(e, scene, index, m => m.ParallaxScale = v),
                        0.005f, 0f, 0.5f, "%.3f", 0.05f, _commandHistory);
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

    private static void EditMaterial(Entity e, Scene scene, int index, Action<Material> apply)
    {
        if (e.MakeMaterialUnique(index))
            scene.MarkDirty();
        apply(e.Materials[index]!);
    }
}
