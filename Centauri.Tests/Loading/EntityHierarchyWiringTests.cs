namespace Centauri.Tests.Loading;

using Centauri.Loading;
using Centauri.World;

// EntityHierarchyWiring is internal (Centauri.csproj grants Centauri.Tests InternalsVisibleTo —
// see Centauri.csproj) and needs no GL context: Entity/Transform are plain C# graph state, so
// this exercises the exact algorithm EntitySetLoader.LoadAll runs at scene-load time without
// booting the engine.
public class EntityHierarchyWiringTests
{
    private static (EntityDefinition def, Entity entity) Node(string name, string? parent = null)
    {
        var entity = new Entity { Name = name };
        var def = new EntityDefinition { Name = name, Parent = parent };
        return (def, entity);
    }

    [Fact]
    public void Wire_LinksChildToNamedParent()
    {
        var parent = Node("Parent");
        var child  = Node("Child", parent: "Parent");
        var built  = new List<(EntityDefinition, Entity)> { parent, child };

        EntityHierarchyWiring.Wire(built);

        Assert.Same(parent.entity.Transform, child.entity.Transform.Parent);
    }

    [Fact]
    public void Wire_LeavesEntityUnparentedWhenParentFieldIsNullOrEmpty()
    {
        var a = Node("A");
        var b = Node("B", parent: "");
        var built = new List<(EntityDefinition, Entity)> { a, b };

        EntityHierarchyWiring.Wire(built);

        Assert.Null(a.entity.Transform.Parent);
        Assert.Null(b.entity.Transform.Parent);
    }

    [Fact]
    public void Wire_IgnoresUnresolvableParentNameWithoutThrowing()
    {
        var child = Node("Child", parent: "DoesNotExist");
        var built = new List<(EntityDefinition, Entity)> { child };

        var exception = Record.Exception(() => EntityHierarchyWiring.Wire(built));

        Assert.Null(exception);
        Assert.Null(child.entity.Transform.Parent);
    }

    // "First match wins" — see EntityHierarchyWiring's own comment: names aren't required to be
    // unique elsewhere in this schema, so a duplicate name must resolve deterministically rather
    // than throwing or picking arbitrarily.
    [Fact]
    public void Wire_FirstMatchWinsOnDuplicateParentNames()
    {
        var firstParent  = Node("Parent");
        var secondParent = Node("Parent");
        var child        = Node("Child", parent: "Parent");
        var built = new List<(EntityDefinition, Entity)> { firstParent, secondParent, child };

        EntityHierarchyWiring.Wire(built);

        Assert.Same(firstParent.entity.Transform, child.entity.Transform.Parent);
        Assert.NotSame(secondParent.entity.Transform, child.entity.Transform.Parent);
    }

    [Fact]
    public void Wire_HandlesAChainOfParentsInAnyDeclarationOrder()
    {
        // Child declared before its parent in the list — must still resolve, since JSON array
        // order isn't required to be a topological order (this is exactly why Wire runs as a
        // second pass after every entity in the file already exists).
        var grandchild = Node("Grandchild", parent: "Child");
        var child       = Node("Child", parent: "Parent");
        var parent      = Node("Parent");
        var built = new List<(EntityDefinition, Entity)> { grandchild, child, parent };

        EntityHierarchyWiring.Wire(built);

        Assert.Same(parent.entity.Transform, child.entity.Transform.Parent);
        Assert.Same(child.entity.Transform, grandchild.entity.Transform.Parent);
    }
}
