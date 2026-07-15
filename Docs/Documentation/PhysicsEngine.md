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

Attach a `RigidBody` component (`Simulation/Physics/RigidBody.cs`). The collision shape is derived
automatically from the entity's model bounds × transform scale — no manual sizing:

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
the last frame, so components added at runtime "just work" — no manual registration call.

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

## 5. Verify it

There's no in-engine test project, but the standalone-harness pattern from `CLAUDE.md` exercises the
whole path with no GL context — a console app referencing `Centauri.csproj` that drops a dynamic box
onto a static ground and asserts it falls and comes to rest (`restY ≈ 1.0` for a 1 m box on a ground
surface at `y = 0.5`). That's how this integration was validated; llvmpipe headless rendering is not
needed since physics is pure CPU.

## 6. Known limitations / next steps

Deliberately scoped as a foundation. In rough priority order:

- **No editor UI yet** — bodies are attached in code, not from the inspector. An inspector section
  (kind/shape/mass, "+ Add RigidBody") is the natural next step.
- **Not serialized** — `RigidBody` doesn't round-trip through the entity-set JSON schema. Fold it in
  alongside the material-override schema revision (see `CLAUDE.md` "Scene loading").
- **No body removal on entity delete** — `PhysicsSystem` registers bodies but doesn't yet release a
  handle when its entity is deleted at runtime. Fine for static scenes; add a removal path before
  relying on live deletion.
- **No kinematic bodies** — only `Dynamic` and `Static`. A `Kinematic` kind (Transform drives the
  body, e.g. moving platforms) is the obvious third.
- **Culling-grid churn** — a moving dynamic body writes its `Transform` every frame, which bumps
  `Scene.Revision` and forces a `CullingSystem` grid rebuild each frame (exactly the case the comment
  in `World/Scene.cs` anticipated). Harmless at current body counts; revisit with an incremental
  grid update if physics scenes get large.
- **Single friction/bounce material** — `NarrowPhaseCallbacks` uses one global material. Per-material
  friction/restitution would key off `CollidableReference` here.
