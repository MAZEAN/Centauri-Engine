namespace Centauri.Tests.World;

using Centauri.World;

// Scene's multi-select surface is pure C# (Entity itself needs no GL/model/material to construct
// — see Entity's all-optional constructor params) — no ImGui, no live scene rendering, so this is
// tested directly rather than only via the Outliner/gizmo/InputSystem call sites that actually
// drive it.
public sealed class SceneSelectionTests
{
    [Fact]
    public void FreshScene_HasNoSelection()
    {
        var scene = new Scene();

        Assert.Null(scene.Selected);
        Assert.Empty(scene.SelectedEntities);
    }

    [Fact]
    public void Select_ReplacesTheWholeSelectionWithJustThatEntity()
    {
        var scene = new Scene();
        var a = new Entity { Name = "A" };
        var b = new Entity { Name = "B" };
        scene.AddEntity(a);
        scene.AddEntity(b);

        scene.ToggleSelect(a);
        scene.ToggleSelect(b);
        Assert.Equal(2, scene.SelectedEntities.Count);

        scene.Select(a);

        Assert.Single(scene.SelectedEntities);
        Assert.Same(a, scene.Selected);
        Assert.True(scene.IsSelected(a));
        Assert.False(scene.IsSelected(b));
    }

    [Fact]
    public void Select_Null_ClearsTheSelection()
    {
        var scene = new Scene();
        var a = new Entity { Name = "A" };
        scene.AddEntity(a);
        scene.Select(a);

        scene.Select(null);

        Assert.Null(scene.Selected);
        Assert.Empty(scene.SelectedEntities);
    }

    [Fact]
    public void ToggleSelect_AddsWhenNotSelected_RemovesWhenSelected()
    {
        var scene = new Scene();
        var a = new Entity { Name = "A" };
        scene.AddEntity(a);

        scene.ToggleSelect(a);
        Assert.True(scene.IsSelected(a));

        scene.ToggleSelect(a);
        Assert.False(scene.IsSelected(a));
        Assert.Empty(scene.SelectedEntities);
    }

    [Fact]
    public void ToggleSelect_DoesNotDisturbOtherSelectedEntities()
    {
        var scene = new Scene();
        var a = new Entity { Name = "A" };
        var b = new Entity { Name = "B" };
        scene.AddEntity(a);
        scene.AddEntity(b);
        scene.Select(a);

        scene.ToggleSelect(b);

        Assert.True(scene.IsSelected(a));
        Assert.True(scene.IsSelected(b));
        Assert.Equal(2, scene.SelectedEntities.Count);
    }

    [Fact]
    public void Selected_IsThePrimarySelection_TheMostRecentlyAddedEntity()
    {
        // The Properties panel and the gizmo's screen anchor both read Scene.Selected as "the one
        // entity" out of a possibly-multi selection — it has to be *some* deterministic entity,
        // not just "whichever one Contains happens to enumerate first."
        var scene = new Scene();
        var a = new Entity { Name = "A" };
        var b = new Entity { Name = "B" };
        var c = new Entity { Name = "C" };
        scene.AddEntity(a);
        scene.AddEntity(b);
        scene.AddEntity(c);

        scene.ToggleSelect(a);
        scene.ToggleSelect(b);
        scene.ToggleSelect(c);

        Assert.Same(c, scene.Selected);
    }

    [Fact]
    public void AddToSelection_IsANoOp_IfAlreadySelected()
    {
        var scene = new Scene();
        var a = new Entity { Name = "A" };
        scene.AddEntity(a);
        scene.Select(a);

        scene.AddToSelection(a);

        Assert.Single(scene.SelectedEntities);
    }

    [Fact]
    public void ClearSelection_EmptiesTheWholeSet()
    {
        var scene = new Scene();
        var a = new Entity { Name = "A" };
        var b = new Entity { Name = "B" };
        scene.AddEntity(a);
        scene.AddEntity(b);
        scene.ToggleSelect(a);
        scene.ToggleSelect(b);

        scene.ClearSelection();

        Assert.Null(scene.Selected);
        Assert.Empty(scene.SelectedEntities);
    }

    [Fact]
    public void RemoveEntity_DropsItFromTheSelection_WithoutDisturbingTheRestOfIt()
    {
        var scene = new Scene();
        var a = new Entity { Name = "A" };
        var b = new Entity { Name = "B" };
        scene.AddEntity(a);
        scene.AddEntity(b);
        scene.ToggleSelect(a);
        scene.ToggleSelect(b);

        scene.RemoveEntity(a);

        Assert.False(scene.IsSelected(a));
        Assert.True(scene.IsSelected(b));
        Assert.Single(scene.SelectedEntities);
    }

    [Fact]
    public void RemoveEntity_ThePrimarySelection_PromotesTheNextMostRecentEntity()
    {
        var scene = new Scene();
        var a = new Entity { Name = "A" };
        var b = new Entity { Name = "B" };
        scene.AddEntity(a);
        scene.AddEntity(b);
        scene.ToggleSelect(a);
        scene.ToggleSelect(b); // b is primary

        scene.RemoveEntity(b);

        Assert.Same(a, scene.Selected);
    }
}
