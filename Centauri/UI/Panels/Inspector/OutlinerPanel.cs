namespace Centauri.UI.Panels.Inspector;

using ImGuiNET;
using System.Numerics;

using World;
using Common;
using Rendering;
using Loading;

// Scene hierarchy — lists every entity (including light-only ones that can't be
// ray-picked in the viewport) and selects on click. Selection drives the
// PropertiesPanel inspector, which already edits lights/materials/transforms.
// The "+" row composes a new entity from the project's registered models (see
// ResourceSystem.ModelIds) via EntitySetLoader.CreateEntity; Delete removes the selection.
public sealed class HierarchyPanel
{
    private const float Width      = 300f;
    private const float Padding    = 10f;
    private const float BgAlpha    = 0.85f;
    private const float BaseHeight = 260f;

    // Cross-referenced by PropertiesPanel to stack beneath this one — a property (not a const)
    // since it needs Widgets.FontScale, set at runtime once the UI font is known.
    public static float Height => Widgets.Scale(BaseHeight);

    private readonly ImFontPtr _font;
    private readonly ResourceSystem _resourceSystem;
    private readonly EntitySetLoader _entitySetLoader;
    private const ImGuiWindowFlags Flags = Widgets.PanelBase;

    // Lazily built once (the registry doesn't change at runtime) rather than allocating a
    // fresh sorted array from ResourceSystem.ModelIds every frame just to feed the combo.
    private string[]? _modelIds;
    private int _selectedModel;

    public HierarchyPanel(ImFontPtr font, ResourceSystem resourceSystem, EntitySetLoader entitySetLoader)
    {
        _font = font;
        _resourceSystem = resourceSystem;
        _entitySetLoader = entitySetLoader;
    }

    public void Render(Scene scene)
    {
        SetupWindow();

        if (!ImGui.Begin("Outliner", Flags))
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

            DrawRow(scene, entities[i]);

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

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 0.6f);
        ImGui.Combo("##addModel", ref _selectedModel, modelIds, modelIds.Length);

        ImGui.SameLine();
        if (ImGui.Button("+ Add"))
        {
            var modelId = modelIds[_selectedModel];
            var entity = _entitySetLoader.CreateEntity(modelId, name: modelId);
            scene.Select(entity);
        }
    }

    private static void DrawRow(Scene scene, Entity entity)
    {
        var selected = ReferenceEquals(scene.Selected, entity);

        var dim = !entity.Enabled;
        if (dim)
            ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);

        if (ImGui.Selectable($"{Tag(entity)}  {entity.Name}", selected))
            scene.Select(entity);

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

    private static void SetupWindow()
    {
        var viewport = ImGui.GetMainViewport();
        var padding  = Widgets.Scale(Padding);

        var anchor = new Vector2(
            viewport.WorkPos.X + viewport.WorkSize.X - padding,
            viewport.WorkPos.Y + padding);

        ImGui.SetNextWindowPos(anchor, ImGuiCond.Always, new Vector2(1f, 0f));
        ImGui.SetNextWindowSize(new Vector2(Widgets.Scale(Width), Height), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(BgAlpha);
    }
}