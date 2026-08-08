namespace Centauri.Simulation.Physics;

using System.Diagnostics;
using System.Numerics;

using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.Constraints;
using BepuUtilities.Memory;

using Config;
using World;

using BepuMesh = BepuPhysics.Collidables.Mesh;
using Model    = Graphics.Geometry.Model;
using MeshData = Graphics.Geometry.MeshData;

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

    // Per-collidable friction, looked up by NarrowPhaseCallbacks on every contact — see
    // PhysicsCallbacks.BodyMaterial and RigidBody.Friction. Constructed with the pool-only
    // overload (before _simulation exists, so it can be handed to NarrowPhaseCallbacks ahead of
    // Simulation.Create) and Initialize()'d against the live Simulation right after.
    private readonly CollidableProperty<BodyMaterial> _materials;

    // Dynamic bodies only: statics never move, so once created they need no per-frame pose
    // read-back — _tracked is what StepFixed/Interpolate iterate.
    private readonly Dictionary<RigidBody, BodyHandle>   _dynamics   = new();
    private readonly Dictionary<RigidBody, BodyHandle>   _kinematics = new();
    private readonly Dictionary<RigidBody, StaticHandle> _statics    = new();
    private readonly Dictionary<RigidBody, TypedIndex>   _shapes     = new();
    private readonly List<RigidBody>                     _tracked    = new();

    // Last entity known to own each currently-registered RigidBody. Sync() diffs this against the
    // entity's *current* GetComponent<RigidBody>() each frame to notice a component that was
    // removed (inspector "Body: None") or replaced — the only way to catch that, since a removed
    // component simply stops showing up when walking scene.Entities.
    private readonly Dictionary<Entity, RigidBody> _byEntity = new();
    private int _lastPurgeRevision = -1;

    // Decoded triangle data for Mesh-shape statics, keyed by Model.SourcePath so re-decoding via
    // Assimp only happens once per distinct on-disk model regardless of how many static entities
    // reference it — see TryGetTriangles. A cached empty array (decode produced no triangles, or a
    // path already failed once) short-circuits future lookups rather than retrying Assimp forever.
    private readonly Dictionary<string, Triangle[]> _meshTriangleCache = new();

    // Stats surfaced to SimulationSystem for the Stats Overlay's "Physics" section — see §PhysicsEngine.md.
    public int   DynamicBodyCount => _tracked.Count;
    public int   StaticBodyCount  => _statics.Count;
    public float LastStepMs       { get; private set; }

    public int BodyCount => _tracked.Count;

    public PhysicsSystem(PhysicsConfig config)
    {
        _config = config;
        _pool   = new BufferPool();

        _materials = new CollidableProperty<BodyMaterial>(_pool);

        var narrow = new NarrowPhaseCallbacks(new SpringSettings(30, 1), _materials);
        var pose   = new PoseIntegratorCallbacks(config.GravityVector);
        var solve  = new SolveDescription(config.SolverVelocityIterations, config.SolverSubsteps);

        _simulation = Simulation.Create(_pool, narrow, pose, solve);
        _materials.Initialize(_simulation);
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

        // Mesh is only meaningful for Static (see RigidBody.BodyShape.Mesh); TryGetTriangles returns
        // null (silently falling back to the Box path below) for anything else — Dynamic/Kinematic
        // Kind, no Model, code-generated geometry with no on-disk SourcePath, or a decode that
        // produced zero triangles.
        var meshTriangles = rb.Kind == BodyKind.Static ? TryGetTriangles(rb.Shape, entity.Model) : null;

        // A Mesh collider uses the model's exact local-space geometry directly, already positioned
        // exactly as the GPU mesh renders it — unlike Box/Sphere/Capsule, which approximate the
        // model with a bounds-derived proxy shape and need re-centring (CenterOffset) to line up
        // with it, a Mesh shape needs none.
        rb.CenterOffset = meshTriangles is not null ? Vector3.Zero : (bounds?.Center ?? Vector3.Zero) * scale;

        // Sphere collapses to a single radius (the largest axis), broadcast to all three, so
        // DrawPhysicsColliders doesn't need to know which shape it's looking at to read this.
        // Capsule and Box both keep the real per-axis half-extents — DrawPhysicsColliders derives
        // a capsule's radius/length from them via RigidBody.CapsuleDimensions, the same call
        // AddShape below uses, so the two can't silently disagree about what shape got built. Mesh
        // keeps the bounds-derived half-extents too, purely for DebugRenderer's fallback box
        // wireframe (see its own comment) — the real collider ignores this field entirely.
        rb.HalfExtents = rb.Shape == BodyShape.Sphere
            ? new Vector3(SphereRadius(halfExtents))
            : halfExtents;

        var bodyPosition    = transform.Position + Vector3.Transform(rb.CenterOffset, transform.Rotation);
        var bodyOrientation = transform.Rotation;

        var shapeIndex = meshTriangles is not null
            ? AddMeshShape(meshTriangles, Abs(scale))
            : AddShape(halfExtents, rb.Shape);
        _shapes[rb] = shapeIndex;

        CollidableReference collidable;

        if (rb.Kind == BodyKind.Static)
        {
            var handle = _simulation.Statics.Add(new StaticDescription(bodyPosition, bodyOrientation, shapeIndex));
            _statics[rb] = handle;
            collidable = new CollidableReference(handle);
        }
        else if (rb.Kind == BodyKind.Kinematic)
        {
            var handle = _simulation.Bodies.Add(BodyDescription.CreateKinematic(
                new RigidPose(bodyPosition, bodyOrientation),
                new CollidableDescription(shapeIndex, 0.1f),
                new BodyActivityDescription(0.01f)));

            _kinematics[rb] = handle;
            collidable = new CollidableReference(CollidableMobility.Kinematic, handle);
        }
        else
        {
            var inertia = ComputeInertia(halfExtents, rb.Shape, rb.Mass);

            var handle = _simulation.Bodies.Add(BodyDescription.CreateDynamic(
                new RigidPose(bodyPosition, bodyOrientation),
                inertia,
                new CollidableDescription(shapeIndex, 0.1f),
                new BodyActivityDescription(0.01f)));

            _dynamics[rb] = handle;
            _tracked.Add(rb);
            collidable = new CollidableReference(CollidableMobility.Dynamic, handle);
        }

        _materials[collidable] = new BodyMaterial { Friction = rb.Friction };

        // Seed both interpolation poses to the current Transform so the first rendered frame doesn't
        // lerp from an identity pose.
        rb.PrevPosition = rb.CurrPosition = transform.Position;
        rb.PrevRotation = rb.CurrRotation = transform.Rotation;
        rb.Registered   = true;
        rb.Dirty        = false;
    }

    private TypedIndex AddShape(Vector3 halfExtents, BodyShape shape) => shape switch
    {
        BodyShape.Sphere  => _simulation.Shapes.Add(new Sphere(SphereRadius(halfExtents))),
        BodyShape.Capsule => _simulation.Shapes.Add(CapsuleShape(halfExtents)),
        _                 => _simulation.Shapes.Add(new Box(halfExtents.X * 2f, halfExtents.Y * 2f, halfExtents.Z * 2f)),
    };

    // Builds a BEPU Mesh collidable from already-decoded local-space triangles: takes a fresh
    // Triangle buffer from the pool (BEPU's Mesh constructor consumes it directly into its own
    // internal BVH, so it can't be a shared/reused buffer across multiple bodies even when they're
    // built from the same cached triangle array) and copies the cached data in. Freed again in
    // Unregister via Shapes.RemoveAndDispose, which returns this buffer (and the BVH's own) to the
    // pool — plain Shapes.Remove would leak them, unlike Box/Sphere/Capsule which own no pool memory.
    private TypedIndex AddMeshShape(Triangle[] triangles, Vector3 scale)
    {
        _pool.Take<Triangle>(triangles.Length, out var buffer);
        triangles.AsSpan().CopyTo(buffer);
        var mesh = new BepuMesh(buffer, in scale, _pool);
        return _simulation.Shapes.Add(mesh);
    }

    // Resolves a Mesh-shape RigidBody's actual triangle data, or null if a Mesh collider isn't
    // buildable for this entity — the single fallback gate AddMeshShape's caller (Register) checks
    // against, so "when does Mesh silently become Box" lives in exactly one place. Kind==Static is
    // checked by the caller, not here, since it's a property of the RigidBody, not the Model.
    private Triangle[]? TryGetTriangles(BodyShape shape, Model? model)
    {
        if (shape != BodyShape.Mesh || model is not { SourcePath.Length: > 0 }) return null;

        if (!_meshTriangleCache.TryGetValue(model.SourcePath, out var triangles))
        {
            triangles = DecodeTriangles(model.SourcePath);
            _meshTriangleCache[model.SourcePath] = triangles;
        }

        return triangles.Length > 0 ? triangles : null;
    }

    // Re-runs Assimp on the model's own source file to recover the local-space vertex positions
    // GPU upload discards — Mesh.cs/Model.cs don't retain CPU-side geometry after the VBO/EBO exist,
    // so this is the only way to get real triangle data back. A rare, load-time-only cost (cached by
    // TryGetTriangles per distinct path), not a per-frame one.
    private static Triangle[] DecodeTriangles(string path)
    {
        var data      = Model.Decode(path);
        var triangles = new List<Triangle>();

        foreach (var mesh in data.Meshes)
            triangles.AddRange(TrianglesFromMesh(mesh));

        return triangles.ToArray();
    }

    // Pulled out of DecodeTriangles as its own pure function (no Assimp, no file I/O) so the actual
    // novel part of a Mesh collider — interpreting a MeshData's interleaved vertex buffer via its
    // index buffer into BEPU Triangles — is unit-testable directly against a synthetic MeshData
    // rather than only against a real decoded asset (Centauri.Tests/Simulation/RigidBodyShapeTests.cs).
    // Positions come straight out of Mesh.cs's own pos+normal+uv+tangent interleave (stride 11) — the
    // same local space the GPU mesh already renders in (node transforms baked in at decode time by
    // Model.ProcessNode/ProcessMesh), so no extra transform is needed here beyond what
    // PhysicsSystem.Register already applies to the body's own pose and the Mesh shape's own Scale.
    internal static Triangle[] TrianglesFromMesh(MeshData mesh)
    {
        const int stride = 11;
        var triangles = new List<Triangle>(mesh.Indices.Length / 3);

        for (var i = 0; i + 2 < mesh.Indices.Length; i += 3)
        {
            triangles.Add(new Triangle(
                VertexPosition(mesh.Vertices, mesh.Indices[i],     stride),
                VertexPosition(mesh.Vertices, mesh.Indices[i + 1], stride),
                VertexPosition(mesh.Vertices, mesh.Indices[i + 2], stride)));
        }

        return triangles.ToArray();
    }

    private static Vector3 VertexPosition(float[] vertices, uint index, int stride)
    {
        var offset = (int)index * stride;
        return new Vector3(vertices[offset], vertices[offset + 1], vertices[offset + 2]);
    }

    private static BodyInertia ComputeInertia(Vector3 halfExtents, BodyShape shape, float mass) => shape switch
    {
        BodyShape.Sphere  => new Sphere(SphereRadius(halfExtents)).ComputeInertia(mass),
        BodyShape.Capsule => CapsuleShape(halfExtents).ComputeInertia(mass),
        _                 => new Box(halfExtents.X * 2f, halfExtents.Y * 2f, halfExtents.Z * 2f).ComputeInertia(mass),
    };

    private static float SphereRadius(Vector3 halfExtents) =>
        MathF.Max(halfExtents.X, MathF.Max(halfExtents.Y, halfExtents.Z));

    private static Capsule CapsuleShape(Vector3 halfExtents)
    {
        var (radius, length) = RigidBody.CapsuleDimensions(halfExtents);
        return new Capsule(radius, length);
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
        else if (_kinematics.Remove(rb, out var kinematicHandle))
        {
            _simulation.Bodies.Remove(kinematicHandle);
        }
        else if (_statics.Remove(rb, out var staticHandle))
        {
            _simulation.Statics.Remove(staticHandle);
        }

        // RemoveAndDispose rather than plain Remove: a Mesh shape owns pool memory (its triangle
        // buffer and BVH — see AddMeshShape) that Remove alone would leak. A no-op superset for
        // Box/Sphere/Capsule, which own none, so it's safe to use unconditionally rather than
        // needing to know which shape kind this particular RigidBody actually built.
        if (_shapes.Remove(rb, out var shapeIndex))
            _simulation.Shapes.RemoveAndDispose(shapeIndex, _pool);

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

        PushKinematics(dt);

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

    // The reverse data flow of a dynamic body: a Kinematic RigidBody's Transform is the source of
    // truth (moved by whatever's driving it — today, only a live inspector/gizmo edit; a future
    // animation/script system would be the same shape), so before each step this writes the
    // Transform's current pose straight into the BEPU body, plus a velocity derived from how far
    // it moved since last step. The velocity matters as much as the pose: a body that's merely
    // teleported to a new position every step still collides correctly (speculative contacts catch
    // it), but contact *response* — how hard a dynamic body gets pushed — comes from the
    // kinematic's velocity, not its position alone. Without this, a "moving platform" would shove
    // things through walls instead of carrying them smoothly.
    private void PushKinematics(float dt)
    {
        foreach (var (rb, handle) in _kinematics)
        {
            var t = rb.Owner.Transform;
            var newPosition    = t.Position + Vector3.Transform(rb.CenterOffset, t.Rotation);
            var newOrientation = t.Rotation;

            var body = _simulation.Bodies[handle];
            var oldPosition    = body.Pose.Position;
            var oldOrientation = body.Pose.Orientation;

            body.Pose.Position    = newPosition;
            body.Pose.Orientation = newOrientation;
            body.Velocity.Linear  = (newPosition - oldPosition) / dt;
            body.Velocity.Angular = AngularVelocityFromDelta(oldOrientation, newOrientation, dt);
        }
    }

    // Standard small-angle finite-difference estimate: the rotation from oldRotation to
    // newRotation, expressed as an angular-velocity vector. Takes the shorter of the two
    // equivalent quaternion paths (q and -q represent the same rotation) so a delta near 180°
    // doesn't get read as spinning the long way around. Accurate for the sub-frame rotation a
    // fixed 60Hz-stepped kinematic body actually produces; not a substitute for true angular
    // velocity integration over a large single step.
    internal static Vector3 AngularVelocityFromDelta(Quaternion oldRotation, Quaternion newRotation, float dt)
    {
        var delta = newRotation * Quaternion.Inverse(oldRotation);
        if (delta.W < 0f) delta = -delta;
        return new Vector3(delta.X, delta.Y, delta.Z) * (2f / dt);
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
