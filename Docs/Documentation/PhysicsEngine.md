# Physics Engine Setup (BEPUphysics2)

Optional. With `physics.enabled = false` in `config.json` (the default), the engine never even
constructs a BEPU `Simulation` — `SimulationSystem` lazily creates it only on the first frame
physics is on — so the engine boots and runs exactly as before. Rigid-body physics runs on a
**fixed timestep** decoupled from the render frame rate, with pose interpolation so motion stays
smooth at any FPS.

## 1. The dependency

Unlike Tracy (a native library built from source), BEPUphysics2 is a pure-managed NuGet package —
no native `.so`/`.dll` to build or deploy, no `runtimes/<rid>/native/` copy step. It's already
referenced in `Centauri/Centauri.csproj`:

```xml
<PackageReference Include="BepuPhysics" Version="2.4.0" />
```

`BepuPhysics` pulls in `BepuUtilities` transitively. A normal build restores both:

```bash
dotnet build Centauri-Engine.sln -c Release
```

If you're adding it to a fresh checkout by hand instead:

```bash
dotnet add Centauri/Centauri.csproj package BepuPhysics --version 2.4.0
```

There is **no** viewer, service, or external tool to install — the whole engine runs in-process.

## 2. Turn it on

In `Centauri/Config/config.json`, add (or edit) the `physics` block — every field has a default,
so an empty `{ "enabled": true }` is enough to get gravity at 60 Hz:

```jsonc
"physics": {
  "enabled": true,               // master switch; false = zero cost, no Simulation created
  "gravity": [0, -9.81, 0],      // m/s², Y-down to match world axes
  "timestepHz": 60,              // fixed simulation rate (step = 1 / timestepHz)
  "solverVelocityIterations": 8, // BEPU solver refinement per substep
  "solverSubsteps": 1,           // more = stiffer stacks/joints, higher cost
  "maxStepsPerFrame": 8          // spiral-of-death cap after a frame hitch
}
```

The fields map 1:1 onto `Config/Settings/PhysicsConfig.cs`.

## 3. Give an entity a body

### From the editor

Select an entity and open the Inspector's **Physics** section. The "Body" dropdown attaches
(`Dynamic`/`Kinematic`/`Static`) or detaches (`None`) a `RigidBody`; once attached, "Shape"
(`Box`/`Sphere`/`Capsule`, plus `Mesh` while `Body` is `Static` — see below), "Friction", and — for
`Dynamic` bodies only — "Mass" are editable. Any change after the initial attach calls
`RigidBody.MarkDirty()`, so `PhysicsSystem` tears down and rebuilds the underlying BEPU body/shape on
its next `Sync()` instead of silently keeping the stale one. Edits persist through Ctrl+S like any
other authored property (see "Scene loading" in `CLAUDE.md`) — see §3.2 below for the on-disk shape.

### From code

Attach a `RigidBody` component (`Simulation/Physics/RigidBody.cs`) directly. The collision shape is
derived automatically from the entity's model bounds × transform scale — no manual sizing:

```csharp
using Centauri.Simulation.Physics;

// A dynamic prop that falls and collides:
entity.AddComponent(new RigidBody { Kind = BodyKind.Dynamic, Shape = BodyShape.Box, Mass = 5f });

// Immovable world geometry (floor, wall) — never moves, collides with dynamics:
ground.AddComponent(new RigidBody { Kind = BodyKind.Static, Shape = BodyShape.Box, Friction = 1.2f });

// A moving platform — its own motion (driven by whatever moves its Transform: today, a live
// inspector/gizmo edit; a future animation/script system would be the same shape) pushes dynamic
// bodies it collides with, but nothing (gravity, contacts) moves *it*:
platform.AddComponent(new RigidBody { Kind = BodyKind.Kinematic, Shape = BodyShape.Box });

// Exact terrain/level geometry instead of a bounds-derived proxy shape — Static only, see the
// "Mesh colliders" subsection below:
terrain.AddComponent(new RigidBody { Kind = BodyKind.Static, Shape = BodyShape.Mesh });
```

- `Kind` — `Dynamic` (moved by the sim; its pose is written back to the `Transform` every frame),
  `Kinematic` (never moved by the sim, but its own Transform-driven motion pushes `Dynamic` bodies —
  see §4), or `Static` (fixed collider, never moves at all).
- `Shape` — `Box` (oriented box from the bounds), `Sphere` (radius = largest bounds half-extent),
  `Capsule` (Y-axis capsule — radius from the largest X/Z half-extent, cylinder length fills the rest
  of the Y extent; see `RigidBody.CapsuleDimensions`), or `Mesh` (exact triangle geometry, `Static`
  only — see below).
- `Mass` — kg, dynamic only. Inertia is computed from mass + shape.
- `Friction` — Coulomb coefficient, every `Kind` (a `Static` floor's surface matters as much as a
  `Dynamic` crate's). Two bodies' `Friction` values combine geometrically (`sqrt(a*b)`) on contact —
  see §5. No `Bounciness`/restitution field — see §5 for why.

`PhysicsSystem.Sync()` runs each frame and registers any entity that has gained a `RigidBody` since
the last frame, so components added at runtime "just work" — no manual registration call. It also
notices a `RigidBody` that was detached (inspector "Body: None", or `Entity.RemoveComponent<RigidBody>()`
from code) or whose owning entity was deleted from the scene entirely, and releases the BEPU
body/shape it was holding — see `PhysicsSystem.Unregister`/`PurgeOrphaned`.

### Mesh colliders: exact geometry for statics

`Box`/`Sphere`/`Capsule` all approximate an entity's model with a bounds-derived proxy shape — fine
for props, wrong for terrain, level geometry, or anything else whose actual silhouette matters (a
box collider around an archway would block the doorway). `Mesh` uses the model's real triangle data
instead, via BEPU's own `Mesh` collidable (`BepuPhysics.Collidables.Mesh`).

The catch: `Mesh.cs`/`Model.cs` don't retain a model's CPU-side vertex/index data once it's uploaded
to the GPU (see `Model.cs`'s own comment on `Meshes`) — a permanent CPU-side copy would cost memory
on every model whether or not it's ever used for physics. Instead, `Model` now remembers the on-disk
path it was decoded from (`Model.SourcePath`/`ModelData.SourcePath`, set in `Model.Decode`), and
`PhysicsSystem.TryGetTriangles` re-runs Assimp against that same path on demand
(`PhysicsSystem.DecodeTriangles`) the first time a `Static` `Mesh` body needs it — cached per
distinct path afterward (`_meshTriangleCache`) so placing the same model as a `Mesh` collider on
multiple entities only pays the decode cost once. `PhysicsSystem.TrianglesFromMesh` — the actual
interleaved-vertex-buffer-to-`Triangle[]` conversion, pulled out as its own pure function — is unit
tested directly (`Centauri.Tests/Simulation/RigidBodyShapeTests.cs`); the full decode-and-build path
is verified via the standalone harness and headless capture (§7).

`Shape = Mesh` silently falls back to `Box` (`PhysicsSystem.Register`) in three cases, so a `Mesh`
selection never leaves an entity with no collider at all: `Kind` isn't `Static` (BEPU's `Mesh` is a
concave, immovable-only shape with no sensible way to integrate a body moving *through* it — convex
decomposition for dynamic mesh colliders is real, separate follow-up work); the entity has no
`Model`; or the `Model` was never loaded from an on-disk source (code-generated geometry —
`SourcePath` empty). The inspector's Shape dropdown only ever offers "Mesh" while `Kind` is
`Static` and resets `Shape` back to `Box` the moment `Kind` changes away from it
(`EntityPhysicsSection`), so this fallback is a hand-authored-JSON safety net in practice, not
something the UI can put you into by accident.

A `Mesh` shape's `CenterOffset` (§3's bounds-derived re-centring every other shape needs) is always
zero — the triangle data is already in the exact local space the GPU mesh renders in, so there's
nothing to re-centre. `DebugRenderer`'s collider-visualization overlay (§6) doesn't draw the real
triangles for a `Mesh` collider, only an approximate bounds-box wireframe, origin-centred rather than
bounds-centred — see its own comment for why.

### 3.2 On-disk shape (entity-set JSON)

`RigidBody` round-trips through the generic component mechanism every other authored `Component`
already uses (`EntityDefinition.Components`, see `ComponentFactory`) — no schema change needed:

```jsonc
{
  "name": "Crate",
  "model": "Assets/Objects/Crate.model",
  "position": [0, 5, 0],
  "components": [
    { "type": "rigidBody", "kind": "dynamic", "shape": "box", "mass": 5.0, "friction": 0.8 }
  ]
}
```

`kind` is `"dynamic"` (default), `"kinematic"`, or `"static"`; `shape` is `"box"` (default),
`"sphere"`, `"capsule"`, or `"mesh"` (`Static` only — see "Mesh colliders" above); `mass` defaults
to `1.0` and is ignored for non-dynamic bodies; `friction` defaults to `1.0` and applies to every
kind.

## 4. How the fixed timestep works

Per frame, `SimulationSystem.Update` (`Simulation/SimulationSystem.cs`):

1. Ticks every entity's components on the **real** frame delta (animations, day/night — these are
   visuals, not simulation, and don't want fixed-step determinism).
2. Banks the frame's real elapsed time into an accumulator, then spends it in whole `1/timestepHz`
   steps — the classic Gaffer *"Fix Your Timestep"* accumulator. Simulation behaviour is therefore
   identical at 30 or 300 FPS.
3. Interpolates each dynamic body between its previous and current fixed-step pose by the leftover
   accumulator fraction, writing the blended pose into the `Transform`. This is what keeps motion
   smooth when the render rate and the fixed rate don't line up.
4. `maxStepsPerFrame` caps the catch-up loop: after a hitch (breakpoint, GC, window drag) the unspent
   backlog is dropped rather than chased with an unbounded burst that would only cause the next hitch.

BEPU is stepped **single-threaded** (`Timestep(dt)` with no `IThreadDispatcher`) — deterministic and
dependency-free. Scenes here are small; a thread dispatcher is the drop-in upgrade if body counts
ever make the step the bottleneck (and pairs naturally with the GL 4.3 work).

### Kinematic bodies: the reverse data flow

A `Dynamic` body's pose flows BEPU → `Transform` (step 3 above). A `Kinematic` body's flows the
other way: before `Simulation.Timestep()` runs, `PhysicsSystem.PushKinematics` writes the *current*
`Transform` position/rotation straight into the BEPU body, plus a velocity derived from how far it
moved since the last fixed step (`PhysicsSystem.AngularVelocityFromDelta` for the rotational part —
a standard small-angle finite-difference estimate, unit-tested directly in
`Centauri.Tests/Simulation/RigidBodyShapeTests.cs`). The velocity matters as much as the position: a
body merely teleported to a new spot every step still collides correctly (BEPU's speculative
contacts catch it), but contact *response* — how hard a `Dynamic` body gets pushed — comes from the
kinematic's velocity, not its position alone. Skip the velocity and a "moving platform" would shove
things through walls instead of carrying them smoothly.

Nothing in this codebase moves a `Kinematic` body's `Transform` automatically yet — today that's a
live inspector/gizmo edit; a future animation/script system would plug into exactly the same place
(anything that sets `Transform.Position`/`Rotation` on a `Kinematic`-`RigidBody` entity before the
next fixed step gets picked up). `Kinematic` bodies are never added to `PhysicsSystem`'s
interpolation-tracked set (`_tracked`, `Dynamic`-only) — their `Transform` is the source of truth
already, so there's nothing to interpolate back into it.

## 5. Per-body friction (and why there's no restitution)

Every `RigidBody` (§3) carries a `Friction` float — a standard Coulomb coefficient, defaulting to
`1f` (BEPU's own general-purpose default), applying to `Dynamic`/`Kinematic`/`Static` alike since
friction is a property of the *surface*, not of whether the simulation happens to move that body.
`PhysicsSystem.Register` copies each body's `Friction` into a `CollidableProperty<BodyMaterial>`
(`_materials`, `Simulation/Physics/PhysicsCallbacks.cs`) indexed by the BEPU `CollidableReference` —
a handle-indexed side table, not a field on the shape itself, so it works uniformly across
dynamic/kinematic/static collidables without needing three separate lookup paths. On every contact,
`NarrowPhaseCallbacks.ConfigureContactManifold` looks up both sides' `BodyMaterial` and combines them
geometrically — `sqrt(a * b)` — before handing the result to BEPU's solver as that contact's
`PairMaterialProperties.FrictionCoefficient`. The geometric mean is the standard combine rule for
exactly this reason: two low-friction surfaces (ice on ice) should stay low, not average toward
something misleadingly medium the way an arithmetic mean would.

Verified with the standalone harness (§7): under an identical constant sideways driving force, a
`Friction = 0.02` box slides ~35 world units over 5 seconds while a `Friction = 3` box slides less
than 1 — the same setup, only the coefficient changed.

### Why there's no restitution/bounciness field

This was attempted and reverted. BEPU2 has no native restitution concept at all — there's no
`Restitution`, `ContactEvent`, or `IContactEventHandler` anywhere in the package (confirmed by
searching its XML docs). Its contact model resolves penetration through a stiffness/damping spring
(`SpringSettings`: frequency + damping ratio), not an elastic-collision coefficient — that spring
controls how firmly overlapping bodies get pushed apart, not how much velocity survives a bounce.

The first pass mapped a 0-1 `Bounciness` field onto that spring's damping ratio, on the theory that a
looser (lower-damping) spring might visibly overshoot and rebound. It doesn't: a ball dropped from
`y = 5` onto a static ground settled to rest at `y ≈ 1.0` with **zero** visible bounce, identically at
`Bounciness = 0` and `Bounciness = 1` (damping ratio floored at `0.02`, the lowest BEPU tolerates). A
real implementation needs a contact-event callback that manually reflects each contacting body's
velocity along the contact normal *after* the solver runs — infrastructure this codebase doesn't have
yet (`IContactEventHandler` or equivalent manual post-step velocity patch). Shipping a `Bounciness`
slider that visibly does nothing would violate this project's own bar for finished work, so it was
pulled rather than left in half-working — see §8 for the follow-up.

## 6. Inspecting physics at runtime

### Stats Overlay

With `physics.enabled = true`, the Stats Overlay (top-left, toggle in the Viewport section or via
`debug.showStatsOverlay`) gets a **Physics** section (collapsed by default, like Culling/Shadows):
dynamic/static body counts and the fixed-step cost for the current frame (`Steps/Frame` — normally 1,
higher after a hitch, 0 below `timestepHz`'s period; `Step Time` — the summed `Simulation.Timestep()`
cost across them). The section is hidden entirely when physics is off rather than showing stale zeros.

### Per-entity live values (Inspector)

The Inspector's **Physics** section (§3) grows a **Live** block under Mass for `Dynamic` bodies:
`Velocity`, `Angular Vel.`, and `Acceleration` — each shown as both the raw vector and its magnitude.
These are read straight off `RigidBody.LinearVelocity`/`AngularVelocity`/`LinearAcceleration`
(`Simulation/Physics/RigidBody.cs`), refreshed every fixed step by `PhysicsSystem.StepFixed`.
`LinearVelocity`/`AngularVelocity` come directly from the BEPU body; `LinearAcceleration` is a
finite difference (`ΔLinearVelocity / fixedDt`) since BEPU doesn't expose acceleration as a native
quantity — for a body only under gravity this settles near the configured `Gravity` vector, and a
landing impact shows up as a large transient spike the step it happens.

### Viewport collider visualization

Viewport section → **Physics Colliders** (`debug.showPhysicsColliders`, off by default) draws a
wireframe box, sphere, or capsule (`Shapes.CapsuleEdges` — two end rings, four silhouette lines, and
half-circle arcs sketching the hemispherical caps; see `RigidBody.CapsuleDimensions` for the
radius/length it's built from) over every registered `RigidBody`, sized and oriented exactly as
`PhysicsSystem.Register` actually built it — magenta = `Dynamic`, blue = `Static`, green =
`Kinematic` (see `ColorPalette`) — plus a yellow arrow along a `Dynamic` body's current
`LinearVelocity`. Lives in `DebugRenderer.DrawPhysicsColliders` alongside the existing
AABB/culling-grid/frustum overlays — same on/off toggle pattern, same immediate-mode line drawer
(`Draw`/`Shapes`). Useful for confirming a collider actually matches the visual mesh (a Sphere shape
on a long thin model, for instance, is easy to get wrong silently — see §8's "no collider-size
feedback" note this closes).

## 7. Verify it

There's no in-engine test project, but the standalone-harness pattern from `CLAUDE.md` exercises the
whole path with no GL context — a console app referencing `Centauri.csproj`. llvmpipe headless
rendering is not needed for any of this since physics is pure CPU; only the last check needs it.

Covered so far:

- A dynamic box dropped from `y = 10` onto a static ground (surface at `y = 0.5`) falls and comes to
  rest at `restY ≈ 1.0` for a 1 m box; a `Capsule`-shaped body with the same (default, modelless)
  half-extents degenerates to a sphere (`radius = 0.5`, cylinder `length = 0`) and rests at the same
  `y ≈ 1.0`, confirming `RigidBody.CapsuleDimensions` and `PhysicsSystem`'s capsule shape-building
  agree with each other.
- A `Kinematic` platform driven 3 world units across 2 seconds of fixed steps correctly pushes a
  `Dynamic` box out of its path via real contact response (not a teleport-through) — confirms
  `PhysicsSystem.PushKinematics`'s velocity derivation (§4), not just its position write.
- Editing a registered body's `Kind` (`Dynamic` → `Kinematic`, and `Dynamic` → `Static`) and calling
  `MarkDirty()` actually rebuilds it: a falling `Dynamic` body switched to `Kinematic` stops
  responding to gravity on the very next `Sync()` and holds its `Transform`-driven position across 60
  further fixed steps, rather than the stale `Dynamic` body silently continuing to fall.
- Under an identical constant sideways driving force, a `Friction = 0.02` box slides roughly 35 world
  units over 5 seconds of fixed steps while a `Friction = 3` box slides less than 1 — confirms §5's
  per-body combine rule produces a real, dramatic difference, not just a wired-up no-op field.
- `Entity.RemoveComponent<RigidBody>()` stops a body from being simulated and doesn't throw on
  subsequent `SimulationSystem.Update` calls.
- Deleting the owning `Entity` from the `Scene` entirely (not just detaching the component) doesn't
  leak a BEPU handle or crash later steps — `PurgeOrphaned` catches it.
- Round-tripping a `{ "type": "rigidBody", ... }` `ComponentDefinition` through `ComponentFactory`
  produces a `RigidBody` with the expected `Kind`/`Shape`/`Mass`/`Friction` — including
  `kind: "kinematic"` and `shape: "capsule"`, not just the pre-existing dynamic/box/sphere cases.
- The full engine still boots and shuts down cleanly headless (`CENTAURI_HEADLESS_FRAMES`) with
  `physics.enabled = true` and the inspector's Physics section compiled in.
- Mid-fall, `LinearVelocity.Y` is substantially negative and `LinearAcceleration.Y` sits near the
  configured gravity (`-9.81`); at rest, `LinearVelocity` decays back to ~0 — confirms §6's Inspector
  readout isn't just wired up but actually tracks real simulated motion.
- `SimulationSystem.PhysicsDynamicBodies`/`PhysicsStaticBodies` match the bodies actually registered.
- A modelless entity-set scene (`components: [{ "type": "rigidBody", ... }]`, no `"model"`) with
  `debug.showPhysicsColliders = true` runs 90 headless frames without crashing.
- `RigidBody.CapsuleDimensions` (radius/length derivation) and `PhysicsSystem.AngularVelocityFromDelta`
  (the kinematic angular-velocity finite-difference formula) are unit tested directly with real
  numeric assertions in `Centauri.Tests/Simulation/RigidBodyShapeTests.cs` — no GL context, no
  standalone harness needed for this pure-math part.
- `PhysicsSystem.TrianglesFromMesh` (the interleaved-vertex-buffer → `Triangle[]` conversion a `Mesh`
  collider is actually built on) is unit tested against a synthetic two-triangle quad — correct
  triangle count, correct vertex positions read through the index buffer, and an empty index buffer
  producing no triangles — without needing Assimp or a real asset (`RigidBodyShapeTests.cs`).
- A `Static` `Mesh` collider built from a real on-disk asset (`Model.Decode` re-run against
  `Model.SourcePath`, exactly as `PhysicsSystem.TryGetTriangles` does it at runtime) respects the
  *actual* triangle geometry, not just its bounding box: a single-triangle floor covering only half
  its own AABB (a right triangle, `(-3,2,-3)`–`(3,2,-3)`–`(-3,2,3)`) catches a dynamic sphere dropped
  onto the covered half (rests at `y ≈ 2.5`, the triangle's `y = 2` plus the sphere's `0.5` radius)
  and lets one dropped onto the *uncovered* half of the same bounding box fall straight through to a
  separate backing floor below (rests at `y ≈ 0.5`) — confirming the collider isn't silently using a
  `Box` approximation of the mesh's AABB, which would have caught both. Verified headless
  (`CENTAURI_HEADLESS_FRAMES`) with a throwaway `.obj` fixture and entity-set JSON, since building the
  real `Model` (not just decoding its `ModelData`) needs a GL context the GL-free standalone harness
  doesn't have.
- Dropping a sphere down the exact centre axis of the full `Testing/Trees/Tree.glb` asset (used
  elsewhere for foliage rendering/LOD work) fell straight through to the ground during this same
  round of manual verification, initially read as a bug — turned out to be correct: that particular
  tree model's trunk is a hollow shell (a common modeling convention — visible surface only, no
  solid interior), so a sphere centred exactly on the trunk's own axis can legitimately never touch
  the wall if the trunk's radius exceeds the sphere's. The controlled single-triangle test above is
  what actually pins the collider's correctness; a real foliage asset's specific geometry is not a
  reliable pass/fail signal on its own.

## 8. Known limitations / next steps

Deliberately scoped as a foundation. In rough priority order:

- **No restitution/bounciness** — see §5. BEPU2's contact model has no elastic-collision coefficient
  to key off of; a real implementation needs a contact-event callback that manually reflects velocity
  along the contact normal post-solve, which this codebase doesn't have.
- **No dynamic/kinematic mesh colliders** — `Mesh` is `Static`-only (see its own subsection under
  §3). BEPU's `Mesh` is a concave shape with no sensible way to move a body *through* — a real
  moving/pushable mesh collider needs convex decomposition (splitting the concave mesh into a
  compound of convex hulls BEPU's solver can actually integrate), which is separate, substantial
  follow-up work, not an extension of what's here.
- **No mesh-collider simplification** — a `Mesh` collider decodes and collides against a model's
  full render-resolution triangle count, whatever that is; there's no separate, simpler collision
  mesh a heavy asset could opt into. Fine for hand-modeled level geometry (typically already
  collision-appropriate), a poor fit for something like dense foliage with hundreds of thousands of
  triangles, where the Assimp re-decode alone (§3's subsection) can take several seconds and the
  resulting BEPU BVH build adds more on top — both one-time, load-time costs, but real ones for a
  large asset.
- **A `Mesh` collider is exactly as solid as its actual geometry, no more** — a shell/hollow-interior
  model (common for game-ready foliage/props — visible surface only, no capped interior) has no
  collision response anywhere that surface doesn't exist, however far "inside" the model's bounds
  that point is. Not a bug to route around (§7's tree-trunk example) — genuinely correct behavior for
  an *exact* mesh collider, but a real authoring gotcha worth knowing about before relying on one for
  gameplay-critical collision.
- **Culling-grid churn** — a moving dynamic (or kinematic) body writes its `Transform` every frame,
  which bumps `Scene.Revision` and forces a `CullingSystem` grid rebuild each frame (exactly the case
  the comment in `World/Scene.cs` anticipated). Harmless at current body counts; revisit with an
  incremental grid update if physics scenes get large. As a side effect, `PhysicsSystem`'s own
  orphan-cleanup sweep (§3) now piggybacks on that same `Scene.Revision` signal, so it's already only
  as expensive as that existing tradeoff, not an additional one.
- **No angular-velocity or torque authoring** — the inspector edits Kind/Shape/Mass/Friction but there's
  no way to give a body initial spin or apply an impulse from the editor; `AngularVelocity` is readable
  (§6) but not writable outside code.
