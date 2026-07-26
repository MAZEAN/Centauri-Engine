namespace Centauri.Tests.Editing;

using System.Numerics;

using Centauri.World;
using Centauri.Editing.Undo;

// TransformCommand/TransformState are pure C# too (World.Transform needs no GL context) — no
// gizmo, no ImGui, just the before/after snapshot pair a completed drag would have captured.
public sealed class TransformCommandTests
{
    [Fact]
    public void Undo_RestoresThePositionRotationAndScale_FromBeforeTheDrag()
    {
        var t = new Transform { Position = new Vector3(5f, 0f, 0f) };
        var before = TransformState.Of(t);

        t.Position = new Vector3(10f, 2f, -3f);
        t.SetRotation(Quaternion.CreateFromYawPitchRoll(0.4f, 0.1f, 0f));
        t.Scale = new Vector3(2f, 2f, 2f);
        var after = TransformState.Of(t);

        var command = new TransformCommand(t, before, after);
        command.Undo();

        Assert.Equal(before.Position, t.Position);
        Assert.Equal(before.Rotation, t.Rotation);
        Assert.Equal(before.Scale, t.Scale);
    }

    [Fact]
    public void Redo_ReappliesTheAfterState()
    {
        var t = new Transform();
        var before = TransformState.Of(t);

        t.Position = new Vector3(1f, 2f, 3f);
        var after = TransformState.Of(t);

        var command = new TransformCommand(t, before, after);
        command.Undo();
        command.Redo();

        Assert.Equal(after.Position, t.Position);
    }

    [Fact]
    public void Undo_RefreshesTheEulerAnglesCache_SoTheInspectorDoesntShowAStaleValue()
    {
        // SetRotation (not the raw Rotation setter) is what keeps Transform.EulerAngles coherent
        // with the actual orientation — see Transform.SetRotation's own comment. TransformCommand
        // must go through it too, or an undo would silently desync the inspector's Rotation rows
        // from the orientation it actually reverted to.
        var t = new Transform();
        t.SetEulerAngles(0f, 90f, 0f);
        var before = TransformState.Of(t);

        t.SetEulerAngles(45f, 0f, 0f);
        var after = TransformState.Of(t);

        var command = new TransformCommand(t, before, after);
        command.Undo();

        var rebuilt = Quaternion.CreateFromYawPitchRoll(
            float.DegreesToRadians(t.EulerAngles.Y),
            float.DegreesToRadians(t.EulerAngles.X),
            float.DegreesToRadians(t.EulerAngles.Z));

        Assert.True(MathF.Abs(Quaternion.Dot(rebuilt, t.Rotation)) > 0.999f);
    }

    [Fact]
    public void TransformState_Equality_IsValueBased()
    {
        var t = new Transform { Position = new Vector3(1f, 2f, 3f) };

        var a = TransformState.Of(t);
        var b = TransformState.Of(t);

        Assert.Equal(a, b);
    }
}
