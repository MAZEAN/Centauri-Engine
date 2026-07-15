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
(`Dynamic`/`Static`) or detaches (`None`) a `RigidBody`; once attached, "Shape" (`Box`/`Sphere`) and
— for `Dynamic` bodies — "Mass" are editable. Any change after the initial attach calls
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
ground.AddComponent(new RigidBody { Kind = BodyKind.Static, Shape = BodyShape.Box });
```

- `Kind` — `Dynamic` (moved by the sim; its pose is written back to the `Transform` every frame) or
  `Static` (fixed collider).
- `Shape` — `Box` (oriented box from the bounds) or `Sphere` (radius = largest bounds half-extent).
- `Mass` — kg, dynamic only. Inertia is computed from mass + shape.

`PhysicsSystem.Sync()` runs each frame and registers any entity that has gained a `RigidBody` since
the last frame, so components added at runtime "just work" — no manual registration call. It also
notices a `RigidBody` that was detached (inspector "Body: None", or `Entity.RemoveComponent<RigidBody>()`
from code) or whose owning entity was deleted from the scene entirely, and releases the BEPU
body/shape it was holding — see `PhysicsSystem.Unregister`/`PurgeOrphaned`.

### 3.2 On-disk shape (entity-set JSON)

`RigidBody` round-trips through the generic component mechanism every other authored `Component`
already uses (`EntityDefinition.Components`, see `ComponentFactory`) — no schema change needed:

```jsonc
{
  "name": "Crate",
  "model": "Assets/Objects/Crate.model",
  "position": [0, 5, 0],
  "components": [
    { "type": "rigidBody", "kind": "dynamic", "shape": "box", "mass": 5.0 }
  ]
}
```

`kind` is `"dynamic"` (default) or `"static"`; `shape` is `"box"` (default) or `"sphere"`; `mass`
defaults to `1.0` and is ignored for static bodies.

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

## 5. Inspecting physics at runtime

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
wireframe box or sphere over every registered `RigidBody`, sized and oriented exactly as
`PhysicsSystem.Register` actually built it (magenta = `Dynamic`, blue = `Static`), plus a yellow
arrow along a `Dynamic` body's current `LinearVelocity`. Lives in `DebugRenderer.DrawPhysicsColliders`
alongside the existing AABB/culling-grid/frustum overlays — same on/off toggle pattern, same
immediate-mode line drawer (`Draw`/`Shapes`). Useful for confirming a collider actually matches the
visual mesh (a Sphere shape on a long thin model, for instance, is easy to get wrong silently — see
§7's "no collider-size feedback" note this closes).

## 6. Verify it

There's no in-engine test project, but the standalone-harness pattern from `CLAUDE.md` exercises the
whole path with no GL context — a console app referencing `Centauri.csproj`. llvmpipe headless
rendering is not needed for any of this since physics is pure CPU; only the last check needs it.

Covered so far:

- A dynamic box dropped from `y = 10` onto a static ground (surface at `y = 0.5`) falls and comes to
  rest at `restY ≈ 1.0` for a 1 m box.
- Editing a registered body's `Kind` (`Dynamic` → `Static`) and calling `MarkDirty()` actually
  rebuilds it: the body freezes in place on the next `Sync()` instead of continuing to fall.
- `Entity.RemoveComponent<RigidBody>()` stops a body from being simulated and doesn't throw on
  subsequent `SimulationSystem.Update` calls.
- Deleting the owning `Entity` from the `Scene` entirely (not just detaching the component) doesn't
  leak a BEPU handle or crash later steps — `PurgeOrphaned` catches it.
- Round-tripping a `{ "type": "rigidBody", ... }` `ComponentDefinition` through `ComponentFactory`
  produces a `RigidBody` with the expected `Kind`/`Shape`/`Mass`.
- The full engine still boots and shuts down cleanly headless (`CENTAURI_HEADLESS_FRAMES`) with
  `physics.enabled = true` and the inspector's Physics section compiled in.
- Mid-fall, `LinearVelocity.Y` is substantially negative and `LinearAcceleration.Y` sits near the
  configured gravity (`-9.81`); at rest, `LinearVelocity` decays back to ~0 — confirms §5's Inspector
  readout isn't just wired up but actually tracks real simulated motion.
- `SimulationSystem.PhysicsDynamicBodies`/`PhysicsStaticBodies` match the bodies actually registered.
- A modelless entity-set scene (`components: [{ "type": "rigidBody", ... }]`, no `"model"`) with
  `debug.showPhysicsColliders = true` runs 90 headless frames without crashing, and the captured
  screenshot shows correctly-shaped box/sphere wireframes at the falling bodies' live positions —
  confirms the collider debug-draw path (§5) end to end, GL calls included, not just that it compiles.

## 7. Known limitations / next steps

Deliberately scoped as a foundation. In rough priority order:

- **No kinematic bodies** — only `Dynamic` and `Static`. A `Kinematic` kind (Transform drives the
  body, e.g. moving platforms) is the obvious third.
- **Culling-grid churn** — a moving dynamic body writes its `Transform` every frame, which bumps
  `Scene.Revision` and forces a `CullingSystem` grid rebuild each frame (exactly the case the comment
  in `World/Scene.cs` anticipated). Harmless at current body counts; revisit with an incremental
  grid update if physics scenes get large. As a side effect, `PhysicsSystem`'s own orphan-cleanup
  sweep (§3) now piggybacks on that same `Scene.Revision` signal, so it's already only as expensive
  as that existing tradeoff, not an additional one.
- **Single friction/bounce material** — `NarrowPhaseCallbacks` uses one global material. Per-material
  friction/restitution would key off `CollidableReference` here.
- **No angular-velocity or torque authoring** — the inspector edits Kind/Shape/Mass but there's no way
  to give a body initial spin or apply an impulse from the editor; `AngularVelocity` is readable (§5)
  but not writable outside code.
