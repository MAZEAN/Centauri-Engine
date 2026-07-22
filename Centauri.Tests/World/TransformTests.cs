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

    // SetRotation (used by the rotate gizmo, which composes an arbitrary world-axis delta) must keep
    // the EulerAngles cache the inspector displays/edits from coherent with the quaternion. Rather
    // than compare extracted angles directly (many valid triples exist, and gimbal cases are
    // ambiguous), round-trip through rotations: the euler SetRotation cached must rebuild the same
    // orientation. |dot| ≈ 1 means "same rotation" (q and -q are the same orientation).
    [Theory]
    [InlineData(0f, 0f, 0f)]
    [InlineData(30f, 45f, 0f)]
    [InlineData(-20f, 120f, 60f)]
    [InlineData(15f, -170f, -80f)]
    [InlineData(89f, 10f, 5f)]   // near — but not at — the pitch gimbal
    public void SetRotation_KeepsEulerAnglesCoherentWithTheQuaternion(float pitch, float yaw, float roll)
    {
        var q = Quaternion.CreateFromYawPitchRoll(
            float.DegreesToRadians(yaw), float.DegreesToRadians(pitch), float.DegreesToRadians(roll));

        var t = new Transform();
        t.SetRotation(q);

        // Rebuild a quaternion from the euler the setter cached; it must be the same orientation.
        var e = t.EulerAngles; // (pitch, yaw, roll) in degrees
        var rebuilt = Quaternion.CreateFromYawPitchRoll(
            float.DegreesToRadians(e.Y), float.DegreesToRadians(e.X), float.DegreesToRadians(e.Z));

        Assert.True(MathF.Abs(Quaternion.Dot(Quaternion.Normalize(q), rebuilt)) > 0.9999f,
            $"euler {e} rebuilt to a different orientation than the source quaternion");
    }

    [Fact]
    public void SetRotation_AtThePitchGimbal_StillReproducesTheOrientation()
    {
        // pitch = +90° is the degenerate case ToEulerDegrees special-cases (roll pinned to 0).
        var q = Quaternion.CreateFromYawPitchRoll(
            float.DegreesToRadians(40f), float.DegreesToRadians(90f), float.DegreesToRadians(25f));

        var t = new Transform();
        t.SetRotation(q);

        var e = t.EulerAngles;
        var rebuilt = Quaternion.CreateFromYawPitchRoll(
            float.DegreesToRadians(e.Y), float.DegreesToRadians(e.X), float.DegreesToRadians(e.Z));

        Assert.True(MathF.Abs(Quaternion.Dot(Quaternion.Normalize(q), rebuilt)) > 0.999f,
            $"gimbal euler {e} rebuilt to a different orientation");
    }
}
