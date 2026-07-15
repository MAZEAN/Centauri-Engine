namespace Centauri.Simulation.Physics;

using System.Numerics;

using World.Components;

public enum BodyKind
{
    Dynamic, // moved by the simulation (gravity, collisions); writes its pose back to the Transform
    Static   // never moves; participates in collision as immovable world geometry (floors, walls)
}

public enum BodyShape
{
    Box,    // oriented box derived from the entity's model bounds × scale
    Sphere  // sphere whose radius is the largest bounds half-extent × scale
}

// Attach to an Entity to give it a physics presence. Deliberately BEPU-type-free: the actual body
// handle and simulation state live in PhysicsSystem, keyed off this component, so the World/scene
// layer never references BepuPhysics. The interpolation fields are written by PhysicsSystem each
// fixed step and read back when it interpolates poses into the Transform for rendering.
public sealed class RigidBody : Component
{
    public BodyKind  Kind  { get; set; } = BodyKind.Dynamic;
    public BodyShape Shape { get; set; } = BodyShape.Box;

    // Mass in kg (dynamic only; ignored for static). Higher mass resists the same contact/impulse
    // more; inertia is derived from mass and the collision shape.
    public float Mass { get; set; } = 1f;

    // Marks Kind/Shape/Mass as changed since the body was last (re)built, so PhysicsSystem tears
    // down and recreates the BEPU body/shape on the next Sync instead of silently ignoring an
    // edit made after the initial registration (e.g. from the inspector's Physics section).
    public void MarkDirty() => Dirty = true;

    // ---- Read-only physical state, updated every fixed step by PhysicsSystem.StepFixed ----
    // (Dynamic bodies only — a Static body never moves, so these stay at their zero default.)
    // For display (inspector "Physics" section) rather than gameplay use: querying BEPU's own
    // BodyReference.Velocity directly would need the same live-simulation access the World/scene
    // layer deliberately doesn't have (see the class comment) — mirroring it here keeps that
    // boundary intact.

    // World-space linear velocity in m/s, read straight from the BEPU body each fixed step.
    public Vector3 LinearVelocity { get; internal set; }

    // World-space angular velocity in rad/s.
    public Vector3 AngularVelocity { get; internal set; }

    // Finite-difference (ΔLinearVelocity / fixedDt) between this step and the previous one — not a
    // BEPU-native quantity, so it's derived here rather than read off the body. For a body only
    // under gravity this settles near the configured Gravity vector; a landing impact shows up as a
    // large transient spike the step it happens, then drops back once resting.
    public Vector3 LinearAcceleration { get; internal set; }

    // ---- Runtime state owned by PhysicsSystem (do not set from gameplay code) ----

    internal bool Registered;
    internal bool Dirty;

    // Local (unrotated) collider half-extents PhysicsSystem last built the body with — Box's own
    // XYZ half-extents, or Sphere's radius broadcast to all three components. Debug-draw only
    // (DebugRenderer.DrawPhysicsColliders); not touched by gameplay code.
    internal Vector3 HalfExtents;

    // Local offset from the Transform origin to the collision-shape centre (model bounds are not
    // necessarily centred on the origin). Kept unrotated; PhysicsSystem rotates it per-frame so the
    // visual origin and the collider stay aligned however the body tumbles.
    internal Vector3 CenterOffset;

    // Two-pose interpolation state: the fixed step advances Curr and copies the old Curr into Prev,
    // so the renderer can lerp between them by the leftover-accumulator fraction (see PhysicsSystem
    // .Interpolate). Both are offset-corrected Transform-space poses, not raw body poses.
    internal Vector3    PrevPosition, CurrPosition;
    internal Quaternion PrevRotation = Quaternion.Identity, CurrRotation = Quaternion.Identity;
}
