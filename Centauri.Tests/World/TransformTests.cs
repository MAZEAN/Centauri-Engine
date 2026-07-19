namespace Centauri.Tests.World;

using System.Numerics;

using Centauri.World;

// Transform is the foundation every entity (and this session's Transform-hierarchy feature) sits
// on — pure C#/System.Numerics, no GL — and its two riskiest pieces are exactly what these cover:
// the cycle guard (a bad re-parent must be refused, not corrupt the graph) and the dirty-flag
// cache (WorldMatrix must actually recompute when it should, and reuse the cached value when it
// shouldn't — a classic source of "works until it doesn't" bugs).
public class TransformTests
{
    [Fact]
    public void Parent_ThrowsWhenSetToSelf()
    {
        var t = new Transform();

        Assert.Throws<InvalidOperationException>(() => t.Parent = t);
    }

    [Fact]
    public void Parent_ThrowsWhenAssignmentWouldCreateACycle()
    {
        var a = new Transform();
        var b = new Transform { Parent = a };
        var c = new Transform { Parent = b };

        // a -> b -> c already; making a's parent = c would close the loop.
        Assert.Throws<InvalidOperationException>(() => a.Parent = c);
    }

    [Fact]
    public void Parent_RejectingACycleLeavesTheGraphUnchanged()
    {
        var a = new Transform();
        var b = new Transform { Parent = a };

        Assert.Throws<InvalidOperationException>(() => a.Parent = b);

        // The failed assignment must not have partially applied — a's parent is still null,
        // and b still has exactly one child (a itself never got added twice, or at all).
        Assert.Null(a.Parent);
        Assert.Same(a, b.Parent);
        Assert.Single(a.Children);
        Assert.Same(b, a.Children[0]);
    }

    [Fact]
    public void Parent_ReparentingRemovesFromThePreviousParentsChildren()
    {
        var oldParent = new Transform();
        var newParent = new Transform();
        var child = new Transform { Parent = oldParent };

        child.Parent = newParent;

        Assert.DoesNotContain(child, oldParent.Children);
        Assert.Contains(child, newParent.Children);
    }

    [Fact]
    public void Parent_SettingTheSameParentAgainDoesNotDuplicateInChildren()
    {
        var parent = new Transform();
        var child = new Transform { Parent = parent };

        child.Parent = parent;

        Assert.Single(parent.Children);
    }

    [Fact]
    public void Parent_SetToNullMovesTransformToRoot()
    {
        var parent = new Transform();
        var child = new Transform { Parent = parent };

        child.Parent = null;

        Assert.Null(child.Parent);
        Assert.Empty(parent.Children);
    }

    [Fact]
    public void WorldMatrix_OfARootTransformEqualsItsLocalMatrix()
    {
        var t = new Transform { Position = new Vector3(1f, 2f, 3f) };

        Assert.Equal(t.LocalMatrix, t.WorldMatrix);
    }

    [Fact]
    public void WorldMatrix_ComposesTranslationThroughAParentChain()
    {
        var parent = new Transform { Position = new Vector3(10f, 0f, 0f) };
        var child  = new Transform { Parent = parent, Position = new Vector3(0f, 5f, 0f) };

        // Child's local offset is (0,5,0); parent sits at (10,0,0) — the child's world position
        // must reflect both, not just its own local transform.
        Assert.Equal(new Vector3(10f, 5f, 0f), child.WorldPosition);
    }

    [Fact]
    public void WorldMatrix_UpdatesAfterTheParentMovesEvenIfAlreadyCachedOnce()
    {
        var parent = new Transform();
        var child  = new Transform { Parent = parent };

        _ = child.WorldMatrix;   // force the cache to populate once, at the parent's old position

        parent.Position = new Vector3(7f, 0f, 0f);

        // If the dirty-flag propagation (MarkWorldDirty walking into _children) were broken, this
        // would still read the stale, pre-move cached value instead of picking up the parent's
        // new position.
        Assert.Equal(new Vector3(7f, 0f, 0f), child.WorldPosition);
    }

    [Fact]
    public void WorldMatrix_StaysCachedWhenNothingChanged()
    {
        var t = new Transform { Position = new Vector3(1f, 1f, 1f) };

        var first = t.WorldMatrix;
        var second = t.WorldMatrix;

        Assert.Equal(first, second);
    }

    [Fact]
    public void Children_ReflectsEveryTransformThatAttachedThisAsParent()
    {
        var parent = new Transform();
        var childA = new Transform { Parent = parent };
        var childB = new Transform { Parent = parent };

        Assert.Equal(2, parent.Children.Count);
        Assert.Contains(childA, parent.Children);
        Assert.Contains(childB, parent.Children);
    }
}
