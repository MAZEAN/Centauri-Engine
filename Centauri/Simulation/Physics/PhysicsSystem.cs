namespace Centauri.Simulation.Physics;

using System.Numerics;

using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.Constraints;
using BepuUtilities.Memory;

using Centauri.Config;
using World;

// Owns the BEPUphysics2 Simulation and the mapping from RigidBody components to their body handles.
// Stepped at a fixed rate by SimulationSystem; between steps it interpolates each dynamic body's
// pose into the owning entity's Transform so rendering stays smooth independent of the physics rate.
//
// Single-threaded on purpose: Timestep() is called with no IThreadDispatcher, which keeps the step
// deterministic and avoids owning a thread pool. Scenes here are small; if body counts ever make
// this the bottleneck, a dispatcher is the drop-in upgrade (and pairs naturally with the GL 4.3 /
// clustered-lighting work that would justify the extra threads).
public sealed class PhysicsSystem : IDisposable
{
    private readonly PhysicsConfig _config;
    private readonly BufferPool    _pool;
    private readonly Simulation    _simulation;

    // Dynamic bodies only: statics never move, so once created they need no per-frame tracking.
    private readonly Dictionary<RigidBody, BodyHandle> _dynamics = new();
    private readonly List<RigidBody>                    _tracked  = new();

    public int BodyCount => _tracked.Count;

    public PhysicsSystem(PhysicsConfig config)
    {
        _config = config;
        _pool   = new BufferPool();

        var narrow = new NarrowPhaseCallbacks(new SpringSettings(30, 1));
        var pose   = new PoseIntegratorCallbacks(config.GravityVector);
        var solve  = new SolveDescription(config.SolverVelocityIterations, config.SolverSubsteps);

        _simulation = Simulation.Create(_pool, narrow, pose, solve);
    }

    // Registers any entity that has picked up a RigidBody component since the last call. Cheap to
    // call every frame — it early-outs on already-registered components. Body removal on entity
    // delete isn't handled yet (see PhysicsEngine.md "Known limitations").
    public void Sync(Scene scene)
    {
        foreach (var entity in scene.Entities)
            if (entity.GetComponent<RigidBody>() is { Registered: false } rb)
                Register(entity, rb);
    }

    private void Register(Entity entity, RigidBody rb)
    {
        var transform = entity.Transform;
        var scale     = transform.Scale;

        // Model bounds are local and not necessarily origin-centred; fold scale in and remember the
        // (unrotated) centre offset so the collider tracks the visual origin however the body rotates.
        var bounds      = entity.Model?.Bounds;
        var halfExtents = (bounds?.Extents ?? new Vector3(0.5f)) * Abs(scale);
        halfExtents     = Vector3.Max(halfExtents, new Vector3(1e-3f)); // no degenerate colliders
        rb.CenterOffset = (bounds?.Center ?? Vector3.Zero) * scale;

        var bodyPosition    = transform.Position + Vector3.Transform(rb.CenterOffset, transform.Rotation);
        var bodyOrientation = transform.Rotation;

        var shapeIndex = rb.Shape == BodyShape.Sphere
            ? _simulation.Shapes.Add(new Sphere(MathF.Max(halfExtents.X, MathF.Max(halfExtents.Y, halfExtents.Z))))
            : _simulation.Shapes.Add(new Box(halfExtents.X * 2f, halfExtents.Y * 2f, halfExtents.Z * 2f));

        if (rb.Kind == BodyKind.Static)
        {
            _simulation.Statics.Add(new StaticDescription(bodyPosition, bodyOrientation, shapeIndex));
        }
        else
        {
            var inertia = rb.Shape == BodyShape.Sphere
                ? new Sphere(MathF.Max(halfExtents.X, MathF.Max(halfExtents.Y, halfExtents.Z))).ComputeInertia(rb.Mass)
                : new Box(halfExtents.X * 2f, halfExtents.Y * 2f, halfExtents.Z * 2f).ComputeInertia(rb.Mass);

            var handle = _simulation.Bodies.Add(BodyDescription.CreateDynamic(
                new RigidPose(bodyPosition, bodyOrientation),
                inertia,
                new CollidableDescription(shapeIndex, 0.1f),
                new BodyActivityDescription(0.01f)));

            _dynamics[rb] = handle;
            _tracked.Add(rb);
        }

        // Seed both interpolation poses to the current Transform so the first rendered frame doesn't
        // lerp from an identity pose.
        rb.PrevPosition = rb.CurrPosition = transform.Position;
        rb.PrevRotation = rb.CurrRotation = transform.Rotation;
        rb.Registered   = true;
    }

    // Advances the simulation by exactly one fixed step. Snapshots the previous pose first so
    // Interpolate() can blend toward the freshly-integrated one.
    public void StepFixed(float dt)
    {
        foreach (var rb in _tracked)
        {
            rb.PrevPosition = rb.CurrPosition;
            rb.PrevRotation = rb.CurrRotation;
        }

        _simulation.Timestep(dt);

        foreach (var rb in _tracked)
        {
            var pose = _simulation.Bodies[_dynamics[rb]].Pose;
            rb.CurrRotation = pose.Orientation;
            // Undo the centre offset applied at registration so the Transform origin, not the
            // collider centre, is what we write back.
            rb.CurrPosition = pose.Position - Vector3.Transform(rb.CenterOffset, pose.Orientation);
        }
    }

    // Writes each dynamic body's pose into its Transform, blended by alpha in [0,1] between the last
    // two fixed steps. Called once per rendered frame with the leftover-accumulator fraction.
    public void Interpolate(float alpha)
    {
        foreach (var rb in _tracked)
        {
            var t = rb.Owner.Transform;
            t.Position = Vector3.Lerp(rb.PrevPosition, rb.CurrPosition, alpha);
            t.Rotation = Quaternion.Slerp(rb.PrevRotation, rb.CurrRotation, alpha);
        }
    }

    private static Vector3 Abs(Vector3 v) => new(MathF.Abs(v.X), MathF.Abs(v.Y), MathF.Abs(v.Z));

    public void Dispose()
    {
        _simulation.Dispose();
        _pool.Clear();
    }
}
