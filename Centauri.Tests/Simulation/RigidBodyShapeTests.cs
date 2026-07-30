namespace Centauri.Tests.Simulation;

using System.Numerics;
using Centauri.Simulation.Physics;

// RigidBody.CapsuleDimensions and PhysicsSystem.AngularVelocityFromDelta are pure math — no BEPU
// simulation, no GL context — unlike almost everything else physics touches, so unlike
// PhysicsSystem itself (verified only by the standalone harness + headless boot, per
// Docs/Documentation/PhysicsEngine.md), these two can be pinned directly.
public class RigidBodyShapeTests
{
    [Theory]
    [InlineData(1f, 1f, 1f, 1f, 0f)]   // cube: radius = 1, no room left for a cylinder segment
    [InlineData(0.5f, 2f, 0.5f, 0.5f, 3f)] // tall narrow box: radius 0.5, length = 2*(2-0.5) = 3
    [InlineData(2f, 0.5f, 2f, 2f, 0f)]  // wide flat box: radius dominates Y entirely, length floors at 0
    public void CapsuleDimensions_DerivesRadiusAndLengthFromHalfExtents(
        float x, float y, float z, float expectedRadius, float expectedLength)
    {
        var (radius, length) = RigidBody.CapsuleDimensions(new Vector3(x, y, z));

        Assert.Equal(expectedRadius, radius, 3);
        Assert.Equal(expectedLength, length, 3);
    }

    [Fact]
    public void CapsuleDimensions_RadiusIsLargerOfXAndZ()
    {
        var (radius, _) = RigidBody.CapsuleDimensions(new Vector3(1f, 10f, 3f));
        Assert.Equal(3f, radius, 3);
    }

    [Fact]
    public void CapsuleDimensions_NeverProducesNegativeLength()
    {
        var (_, length) = RigidBody.CapsuleDimensions(new Vector3(5f, 0.1f, 5f));
        Assert.True(length >= 0f);
    }

    [Fact]
    public void AngularVelocityFromDelta_NoRotation_IsZero()
    {
        var v = PhysicsSystem.AngularVelocityFromDelta(Quaternion.Identity, Quaternion.Identity, 1f / 60f);
        Assert.Equal(Vector3.Zero, v);
    }

    [Fact]
    public void AngularVelocityFromDelta_QuarterTurnOverOneSecond_MatchesExpectedMagnitude()
    {
        // The formula is 2*sin(angle/2)/dt, not angle/dt — an exact readout of the quaternion's
        // own vector part, not a small-angle approximation of the true angle (they agree closely
        // for small angles, which is the regime a per-fixed-step kinematic delta actually lives
        // in, but a 90-degree single-step turn makes the gap visible: 2*sin(45deg) ≈ 1.414,
        // noticeably below the "true" pi/2 ≈ 1.571 the rotation angle itself would suggest).
        var quarterTurn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);

        var v = PhysicsSystem.AngularVelocityFromDelta(Quaternion.Identity, quarterTurn, 1f);

        Assert.Equal(0f, v.X, 3);
        Assert.Equal(2f * MathF.Sin(MathF.PI / 4f), v.Y, 3);
        Assert.Equal(0f, v.Z, 3);
    }

    [Fact]
    public void AngularVelocityFromDelta_TakesShorterPathNearHalfTurn()
    {
        // Same rotation expressed via q and -q (equivalent quaternions) must produce the same
        // angular velocity — the sign-flip guard is what prevents a near-180-degree delta from
        // reading as spinning the "long way around" depending on which of the two equivalent
        // representations the caller happened to pass in.
        var rot    = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 3f); // just under pi
        var negRot = -rot;

        var v1 = PhysicsSystem.AngularVelocityFromDelta(Quaternion.Identity, rot, 1f);
        var v2 = PhysicsSystem.AngularVelocityFromDelta(Quaternion.Identity, negRot, 1f);

        Assert.Equal(v1.X, v2.X, 3);
        Assert.Equal(v1.Y, v2.Y, 3);
        Assert.Equal(v1.Z, v2.Z, 3);
    }
}
