namespace Centauri.Tests.UI;

using System.Numerics;

using Centauri.Config;
using Centauri.UI.Gizmos;
using Centauri.World;

// TransformGizmo's projection + hit-test are pure math, no ImGui/GL — and the projection is the
// part most prone to a silent sign/axis error (a flipped Y or a row-vs-column-vector mismatch
// draws every handle offset from where clicks actually land). The load-bearing invariant is that
// the gizmo's own Project agrees with Camera.ScreenPointToRay (what picking uses): a world point
// projected to a screen pixel, then turned back into a ray through that pixel, must yield a ray
// that passes through the original point. If those two ever disagree, handles render in one place
// and respond to the cursor in another — so this pins them together.
public sealed class TransformGizmoTests
{
    private static readonly Vector2 Viewport = new(1280f, 720f);

    // Mirrors how EnvironmentLoader builds a camera: config + look direction, aspect set from a
    // framebuffer size. yaw -90 / pitch 0 looks down -Z from +Z, the default scene framing.
    private static Camera MakeCamera()
    {
        var cam = new Camera(new CameraConfig(), "Test", new Vector3(0, 0, 3),
            Vector3.UnitY, yaw: -90f, pitch: 0f);
        cam.SetAspectRatio(new Silk.NET.Maths.Vector2D<int>(1280, 720));
        return cam;
    }

    private static Vector2 ProjectViaGizmo(Camera cam, Vector3 world)
    {
        var viewProj = cam.GetViewMatrix() * cam.GetProjectionMatrixRaw();
        Assert.True(TransformGizmo.Project(world, viewProj, Viewport, out var screen));
        return screen;
    }

    [Fact]
    public void Project_PutsAPointInFrontOfTheCameraInsideTheViewport()
    {
        var cam = MakeCamera();

        var screen = ProjectViaGizmo(cam, Vector3.Zero); // origin is dead ahead

        Assert.InRange(screen.X, 0f, Viewport.X);
        Assert.InRange(screen.Y, 0f, Viewport.Y);
        // dead-centre framing → within a pixel of the viewport middle
        Assert.Equal(Viewport.X / 2f, screen.X, tolerance: 1f);
        Assert.Equal(Viewport.Y / 2f, screen.Y, tolerance: 1f);
    }

    [Fact]
    public void Project_ReturnsFalseForAPointBehindTheCamera()
    {
        var cam = MakeCamera(); // at z=3 looking toward -z

        // Behind the camera (further along +Z than the eye) has w <= 0 after view*proj.
        var viewProj = cam.GetViewMatrix() * cam.GetProjectionMatrixRaw();
        Assert.False(TransformGizmo.Project(new Vector3(0, 0, 10), viewProj, Viewport, out _));
    }

    [Fact]
    public void Project_MapsWorldAxesToTheExpectedScreenDirections()
    {
        var cam = MakeCamera();

        var origin = ProjectViaGizmo(cam, Vector3.Zero);
        var plusX  = ProjectViaGizmo(cam, new Vector3(1, 0, 0));
        var plusY  = ProjectViaGizmo(cam, new Vector3(0, 1, 0));

        // +X world is camera-right → screen X increases; screen Y ~unchanged.
        Assert.True(plusX.X > origin.X + 1f);
        Assert.Equal(origin.Y, plusX.Y, tolerance: 1f);

        // +Y world is up → screen Y *decreases* (screen Y grows downward); screen X ~unchanged.
        Assert.True(plusY.Y < origin.Y - 1f);
        Assert.Equal(origin.X, plusY.X, tolerance: 1f);
    }

    [Fact]
    public void Project_AgreesWithScreenPointToRay_SoHandlesLandWhereClicksDo()
    {
        var cam = MakeCamera();

        // A handful of off-axis points in front of the camera.
        foreach (var world in new[]
                 {
                     new Vector3(0f, 0f, 0f),
                     new Vector3(0.5f, 0.75f, -1f),
                     new Vector3(-1.2f, 0.3f, -2.5f),
                     new Vector3(0.8f, -0.6f, 0.5f),
                 })
        {
            var screen = ProjectViaGizmo(cam, world);

            var ray = cam.ScreenPointToRay(screen, Viewport);

            // The point must lie on the ray: its closest approach to the ray is ~zero.
            var toPoint = world - ray.Origin;
            var dir     = Vector3.Normalize(ray.Direction);
            var perp    = toPoint - dir * Vector3.Dot(toPoint, dir);

            Assert.True(perp.Length() < 1e-2f,
                $"point {world} projected to {screen} but the ray back through it missed by {perp.Length():0.000}");
        }
    }

    [Theory]
    [InlineData(5f, 0f, 0f)]     // on the segment (midpoint) → distance 0
    [InlineData(5f, 4f, 4f)]     // directly above the midpoint → perpendicular distance
    [InlineData(-3f, 0f, 3f)]    // past the 'a' endpoint → clamps to |a - p|
    [InlineData(13f, 0f, 3f)]    // past the 'b' endpoint → clamps to |b - p|
    public void DistanceToSegment_MatchesHandComputedGeometry(float px, float py, float expected)
    {
        var a = new Vector2(0f, 0f);
        var b = new Vector2(10f, 0f);

        var d = TransformGizmo.DistanceToSegment(new Vector2(px, py), a, b);

        Assert.Equal(expected, d, tolerance: 1e-4f);
    }

    // --- ComposeWorldRotation: the rotate gizmo composes a world-axis delta onto the grabbed
    //     orientation. These pin the (easy-to-get-backwards) quaternion multiplication order to the
    //     geometry: a world-axis rotation must act in the *world* frame regardless of the object's
    //     current orientation. ---

    private static void AssertVectorsClose(Vector3 expected, Vector3 actual)
    {
        Assert.True((expected - actual).Length() < 1e-4f, $"expected {expected}, got {actual}");
    }

    [Fact]
    public void ComposeWorldRotation_FromIdentity_IsAPlainAxisAngle()
    {
        // +90° about world Y sends +Z to +X (right-hand rule).
        var q = TransformGizmo.ComposeWorldRotation(Quaternion.Identity, Vector3.UnitY, MathF.PI / 2f);
        AssertVectorsClose(Vector3.UnitX, Vector3.Transform(Vector3.UnitZ, q));
    }

    [Fact]
    public void ComposeWorldRotation_AppliesTheDeltaInTheWorldFrame_NotTheObjectFrame()
    {
        // Start already rotated +90° about world X: that sends +Y to +Z.
        var start = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2f);
        AssertVectorsClose(Vector3.UnitZ, Vector3.Transform(Vector3.UnitY, start));

        // Now add +90° about world Y. It must act in world space: +Z then goes to +X, so the
        // object's +Y ends up at +X. (If the delta were applied in the object's local frame instead,
        // this would land somewhere else — that's exactly the bug this guards against.)
        var composed = TransformGizmo.ComposeWorldRotation(start, Vector3.UnitY, MathF.PI / 2f);
        AssertVectorsClose(Vector3.UnitX, Vector3.Transform(Vector3.UnitY, composed));
    }

    [Fact]
    public void ComposeWorldRotation_ZeroAngle_LeavesTheStartUntouched()
    {
        var start = Quaternion.CreateFromYawPitchRoll(0.6f, -0.3f, 1.1f);
        var composed = TransformGizmo.ComposeWorldRotation(start, Vector3.UnitZ, 0f);
        Assert.True(Quaternion.Dot(start, composed) > 0.9999f);
    }
}
