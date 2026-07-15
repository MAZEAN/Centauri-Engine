namespace Centauri.Simulation.Physics;

using System.Diagnostics;
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

    // Dynamic bodies only: statics never move, so once created they need no per-frame pose
    // read-back — _tracked is what StepFixed/Interpolate iterate.
    private readonly Dictionary<RigidBody, BodyHandle>   _dynamics = new();
    private readonly Dictionary<RigidBody, StaticHandle> _statics  = new();
    private readonly Dictionary<RigidBody, TypedIndex>   _shapes   = new();
    private readonly List<RigidBody>                     _tracked  = new();

    // Last entity known to own each currently-registered RigidBody. Sync() diffs this against the
    // entity's *current* GetComponent<RigidBody>() each frame to notice a component that was
    // removed (inspector "Body: None") or replaced — the only way to catch that, since a removed
    // component simply stops showing up when walking scene.Entities.
    private readonly Dictionary<Entity, RigidBody> _byEntity = new();
    private int _lastPurgeRevision = -1;

    // Stats surfaced to SimulationSystem for the Stats Overlay's "Physics" section — see §PhysicsEngine.md.
    public int   DynamicBodyCount => _tracked.Count;
    public int   StaticBodyCount  => _statics.Count;
    public float LastStepMs       { get; private set; }

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

    // Registers any entity that has picked up a RigidBody component since the last call, rebuilds
    // any body whose Kind/Shape/Mass changed after registration (RigidBody.MarkDirty — e.g. an
    // inspector edit), and releases bodies whose component was removed or whose entity was deleted
    // from the scene. Cheap to call every frame: registration/dirty-check early-out per entity, and
    // the deletion sweep only runs when Scene.Revision has actually moved since the last check.
    public void Sync(Scene scene)
    {
        foreach (var entity in scene.Entities)
        {
            var current = entity.GetComponent<RigidBody>();
            _byEntity.TryGetValue(entity, out var prior);

            if (prior != null && !ReferenceEquals(prior, current))
            {
                Unregister(prior);
                _byEntity.Remove(entity);
            }

            if (current is null) continue;

            if (current.Registered && current.Dirty)
                Unregister(current);

            if (!current.Registered)
            {
                Register(entity, current);
                _byEntity[entity] = current;
            }
        }

        PurgeOrphaned(scene);
    }

    // Catches the case Sync()'s per-entity loop can't: an entity deleted from the scene entirely
    // (EntitySetLoader.DeleteEntity et al.) simply stops appearing in scene.Entities, so its
    // RigidBody's body/shape would otherwise leak in the BEPU simulation forever. Gated on
    // Scene.Revision so a static scene doesn't pay a HashSet build every frame — Revision already
    // bumps on add/remove/transform-move, a safe superset of "an entity might have disappeared".
    private void PurgeOrphaned(Scene scene)
    {
        if (_byEntity.Count == 0 || scene.Revision == _lastPurgeRevision) return;
        _lastPurgeRevision = scene.Revision;

        List<Entity>? stale = null;
        var live = new HashSet<Entity>(scene.Entities);
        foreach (var entity in _byEntity.Keys)
            if (!live.Contains(entity))
                (stale ??= new List<Entity>()).Add(entity);

        if (stale is null) return;
        foreach (var entity in stale)
        {
            Unregister(_byEntity[entity]);
            _byEntity.Remove(entity);
        }
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

        // Sphere collapses to a single radius (the largest axis) — broadcast to all three so
        // DrawPhysicsColliders doesn't need to know which shape it's looking at to read this.
        rb.HalfExtents = rb.Shape == BodyShape.Sphere
            ? new Vector3(MathF.Max(halfExtents.X, MathF.Max(halfExtents.Y, halfExtents.Z)))
            : halfExtents;

        var bodyPosition    = transform.Position + Vector3.Transform(rb.CenterOffset, transform.Rotation);
        var bodyOrientation = transform.Rotation;

        var shapeIndex = rb.Shape == BodyShape.Sphere
            ? _simulation.Shapes.Add(new Sphere(MathF.Max(halfExtents.X, MathF.Max(halfExtents.Y, halfExtents.Z))))
            : _simulation.Shapes.Add(new Box(halfExtents.X * 2f, halfExtents.Y * 2f, halfExtents.Z * 2f));
        _shapes[rb] = shapeIndex;

        if (rb.Kind == BodyKind.Static)
        {
            _statics[rb] = _simulation.Statics.Add(new StaticDescription(bodyPosition, bodyOrientation, shapeIndex));
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
        rb.Dirty        = false;
    }

    // Releases whatever BEPU state a registered RigidBody currently owns — body/static handle plus
    // its shape — and marks it unregistered again. Used both for a real removal (component detached,
    // entity deleted) and as the "tear down" half of a Dirty rebuild, where Sync() immediately
    // re-registers afterward with the component's now-current Kind/Shape/Mass.
    private void Unregister(RigidBody rb)
    {
        if (_dynamics.Remove(rb, out var bodyHandle))
        {
            _simulation.Bodies.Remove(bodyHandle);
            _tracked.Remove(rb);
        }
        else if (_statics.Remove(rb, out var staticHandle))
        {
            _simulation.Statics.Remove(staticHandle);
        }

        if (_shapes.Remove(rb, out var shapeIndex))
            _simulation.Shapes.Remove(shapeIndex);

        rb.Registered = false;
        rb.Dirty      = false;
    }

    // Advances the simulation by exactly one fixed step. Snapshots the previous pose first so
    // Interpolate() can blend toward the freshly-integrated one. Also refreshes each dynamic body's
    // Linear/AngularVelocity and the finite-difference LinearAcceleration — read by the inspector's
    // Physics section and (for LastStepMs) the Stats Overlay's Physics section.
    public void StepFixed(float dt)
    {
        foreach (var rb in _tracked)
        {
            rb.PrevPosition = rb.CurrPosition;
            rb.PrevRotation = rb.CurrRotation;
        }

        var sw = Stopwatch.StartNew();
        _simulation.Timestep(dt);
        LastStepMs = (float)sw.Elapsed.TotalMilliseconds;

        foreach (var rb in _tracked)
        {
            var body = _simulation.Bodies[_dynamics[rb]];
            var pose = body.Pose;
            rb.CurrRotation = pose.Orientation;
            // Undo the centre offset applied at registration so the Transform origin, not the
            // collider centre, is what we write back.
            rb.CurrPosition = pose.Position - Vector3.Transform(rb.CenterOffset, pose.Orientation);

            var linearVelocity = body.Velocity.Linear;
            rb.LinearAcceleration = (linearVelocity - rb.LinearVelocity) / dt;
            rb.LinearVelocity     = linearVelocity;
            rb.AngularVelocity    = body.Velocity.Angular;
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
