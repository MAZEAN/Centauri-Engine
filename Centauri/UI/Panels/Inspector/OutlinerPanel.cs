namespace Centauri.UI.Panels.Inspector;

using ImGuiNET;
using System.Numerics;

using World;
using Config;
using Layout;
using Rendering;
using Loading;
using Editing.Undo;

// Scene hierarchy — lists every entity (including light-only ones that can't be
// ray-picked in the viewport) and selects on click. Selection drives the
// PropertiesPanel inspector, which already edits lights/materials/transforms.
// The "+" row composes a new entity from the project's registered models and materials (see
// ResourceSystem.ModelIds/MaterialIds) via EntitySetLoader.CreateEntity; Delete removes the
// selection. Changing an existing entity's material lives in EntityInspectorSection instead.
// Docked into EditorLayout's Outliner rect (see EditorLayout.cs), stacked directly above
// PropertiesPanel — the two touch with no gap, part of the Edit workspace's exact tiling.
internal sealed class HierarchyPanel
{
    private readonly ImFontPtr _font;
    private readonly AppConfig _config;
    private readonly ResourceSystem _resourceSystem;
    private readonly EntitySetLoader _entitySetLoader;
    private readonly CommandHistory _commandHistory;

    // Lazily built once (the registry doesn't change at runtime) rather than allocating a
    // fresh sorted array from ResourceSystem.ModelIds every frame just to feed the combo.
    private string[]? _modelIds;
    private int _selectedModel;

    // Index 0 is always "(Default)" — placing a model without picking a material here falls
    // back to the usual resolution chain (the model's own default binding, else DefaultMaterial)
    // exactly as if CreateEntity's materialId had been left null.
    private string[]? _materialIds;
    private int _selectedMaterial;

    // The row a Shift-range-select extends from — set on every plain or Ctrl click (so the anchor
    // "follows" the click, same convention Windows Explorer/most multi-select lists use), left
    // alone by Shift-clicks themselves (so repeated Shift-clicks keep adjusting the range from the
    // same anchor rather than moving it each time). -1 means no anchor yet — a Shift-click before
    // any other click is currently just treated as a plain click (see DrawRow).
    private int _selectionAnchor = -1;

    public HierarchyPanel(ImFontPtr font, AppConfig config, ResourceSystem resourceSystem, EntitySetLoader entitySetLoader, CommandHistory commandHistory)
    {
        _font = font;
        _config = config;
        _resourceSystem = resourceSystem;
        _entitySetLoader = entitySetLoader;
        _commandHistory = commandHistory;
    }

    public void Render(Scene scene, LayoutRect rect)
    {
        PanelHost.Place(rect, bgAlpha: _config.ImGui.OutlinerAlpha);

        if (!ImGui.Begin("Outliner", PanelHost.DockedFlags))
        {
            ImGui.End();
            return;
        }

        ImGui.PushFont(_font);

        DrawAddRow(scene);
        ImGui.Separator();

        ImGui.BeginChild("entities", new Vector2(0, 0));

        var entities = scene.Entities;
        for (var i = 0; i < entities.Count; i++)
        {
            ImGui.PushID(i);

            DrawRow(scene, entities, i);

            ImGui.PopID();
        }

        ImGui.EndChild();

        ImGui.PopFont();

        ImGui.End();
    }

    private void DrawAddRow(Scene scene)
    {
        var modelIds = _modelIds ??= _resourceSystem.ModelIds.ToArray();
        if (modelIds.Length == 0)
        {
            ImGui.TextDisabled("No models registered under Assets/Objects");
            return;
        }

        var materialIds = _materialIds ??= BuildMaterialOptions();

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 0.6f);
        ImGui.Combo("##addModel", ref _selectedModel, modelIds, modelIds.Length);

        ImGui.SameLine();
        if (ImGui.Button("+ Add"))
        {
            var modelId = modelIds[_selectedModel];
            var materialId = _selectedMaterial == 0 ? null : materialIds[_selectedMaterial];
            var entity = _entitySetLoader.CreateEntity(modelId, materialId, name: modelId);

            scene.Select(entity);
            _selectionAnchor = scene.Entities.Count - 1; // Scene.AddEntity appends, so this is `entity`
            _commandHistory.Push(new CreateEntityCommand(_entitySetLoader, entity, modelId, materialId, modelId));
        }

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        ImGui.Combo("##addMaterial", ref _selectedMaterial, materialIds, materialIds.Length);
    }

    private string[] BuildMaterialOptions()
    {
        var ids = _resourceSystem.MaterialIds.ToArray();
        var options = new string[ids.Length + 1];
        options[0] = "(Default)";
        Array.Copy(ids, 0, options, 1, ids.Length);
        return options;
    }

    private void DrawRow(Scene scene, IReadOnlyList<Entity> entities, int index)
    {
        var entity   = entities[index];
        var selected = scene.IsSelected(entity);

        var dim = !entity.Enabled;
        if (dim)
            ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);

        if (ImGui.Selectable($"{Tag(entity)}  {entity.Name}", selected))
        {
            var io = ImGui.GetIO();

            if (io.KeyShift && _selectionAnchor >= 0)
            {
                // Range-select from the anchor to here, inclusive — replaces the selection rather
                // than adding to it (matching Explorer/Blender), so a Shift-click after some
                // unrelated Ctrl-clicks doesn't leave stray entities selected outside the range.
                scene.ClearSelection();
                var lo = Math.Min(_selectionAnchor, index);
                var hi = Math.Max(_selectionAnchor, index);
                for (var i = lo; i <= hi; i++)
                    scene.AddToSelection(entities[i]);
            }
            else if (io.KeyCtrl)
            {
                scene.ToggleSelect(entity);
                _selectionAnchor = index;
            }
            else
            {
                scene.Select(entity);
                _selectionAnchor = index;
            }
        }

        if (dim)
            ImGui.PopStyleColor();
    }

    private static string Tag(Entity e) => e switch
    {
        { Light: DirectionalLight } => "[Sun]",
        { Light: PointLight }       => "[Point]",
        { Light: SpotLight }        => "[Spot]",
        { Model: not null }         => "[Mesh]",
        _                           => "[Empty]"
    };
}