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

    // ---- Runtime state owned by PhysicsSystem (do not set from gameplay code) ----

    internal bool Registered;

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
