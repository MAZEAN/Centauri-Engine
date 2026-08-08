# Automated Tests (`Centauri.Tests`)

A real xunit project — `Centauri.Tests/Centauri.Tests.csproj`, part of `Centauri-Engine.sln`.
Formalizes the throwaway standalone-console-project pattern this repo used ad hoc before (see
CLAUDE.md's own note on it) into something that runs on every change instead of only when someone
remembers to write a scratch harness. Runs in CI on every push/PR to `main` — see §5.

## 1. Run the tests

```bash
dotnet test Centauri.Tests/Centauri.Tests.csproj -c Release
```

or, from the solution root, to build + test everything in one shot:

```bash
dotnet test Centauri-Engine.sln -c Release
```

Filter to one class or method (standard `dotnet test --filter` syntax):

```bash
dotnet test Centauri.Tests/Centauri.Tests.csproj --filter "FullyQualifiedName~CascadeBuilderTests"
dotnet test Centauri.Tests/Centauri.Tests.csproj --filter "FullyQualifiedName~Wire_FirstMatchWinsOnDuplicateParentNames"
```

No GL context, no window, no Xvfb needed for anything currently in this project — every test here
targets pure-CPU logic. Runs in well under a second.

## 2. What's covered so far

| Suite | Targets | What it actually checks |
|---|---|---|
| `Shadows/CascadeBuilderTests.cs` | `Rendering/Shadows/CascadeBuilder.cs` | Cascade-count clamping to `[1, MaxCascades]`, strictly-increasing split depths, the `reuse`-array optimization (same/different instance as appropriate), **determinism** for identical inputs (bit-identical `Matrix`/`Radius`/`DepthRange`), no non-finite output. |
| `Loading/EntityHierarchyWiringTests.cs` | `Loading/EntitySets/EntityHierarchyWiring.cs` | Parent resolution by name, a null/empty `Parent` field being a no-op, an unresolvable parent name not throwing, first-match-wins on duplicate names, and declaration-order independence (a child listed before its own parent in the source array still resolves — this is *why* `Wire` runs as a second pass after every entity in a file already exists). |
| `Rendering/MaterialRegistryTests.cs` | `Rendering/MaterialRegistry.cs` | `extends` inheritance (a child inheriting fields it doesn't set, overriding ones it does, merging correctly through a multi-level chain), cycle detection (both direct A↔B and self-referencing), and `.mat` `path` texture-prefixing (bare filenames get prefixed, already-qualified paths are left alone, no `path` field means no rewriting at all). |
| `World/TransformTests.cs` | `World/Transform.cs` | The re-parent cycle guard (self-parent, an indirect ancestor cycle, and that a *rejected* cycle leaves the graph exactly as it was — no partial mutation), re-parenting correctly removing from the old parent's `Children`, no duplicate-children on a same-parent no-op, `WorldMatrix` composing correctly through a parent chain, that `WorldMatrix` actually recomputes after the *parent* moves even when the child's value was already cached once (the one most prone to "works until it doesn't" bugs), and **`SetRotation` euler coherence** — that the `EulerAngles` cache the rotate gizmo relies on rebuilds the same orientation as the quaternion it set, across a range of angles and at the ±90° pitch gimbal (compared as rotations, not raw angles, since many euler triples are equivalent). |
| `UI/TransformGizmoTests.cs` | `UI/Gizmos/TransformGizmo.cs` | The gizmo's world→screen `Project` (a front-of-camera point lands in-viewport, dead-centre framing hits the middle, a behind-camera point returns `false`, world axes map to the expected screen directions), its `DistanceToSegment` hit-test against hand-computed geometry, and **`ComposeWorldRotation`** (a world-axis rotate delta must act in the *world* frame regardless of the object's orientation — this test *caught* the backwards System.Numerics multiply order and stayed the fix). The load-bearing projection test is the **round-trip**: `Project` a world point → screen pixel → `Camera.ScreenPointToRay` back → a ray that still passes through the original point, pinning the gizmo's *drawing* projection to the viewport's *picking* projection so handles can't drift from where clicks land. The mouse-drag interaction itself needs a live ImGui frame, so it's verified visually headless, not here. See `Docs/Documentation/Gizmos.md`. |
| `UI/EditorLayoutTests.cs` | `UI/Layout/EditorLayout.cs` | The docked editor layout's geometry across a 72-combination matrix (9 resolutions from 640×480 to 3840×2160 × 2 work-area origins × 4 UI scales): the Edit workspace's five regions **tile the work area exactly** (every shared edge between neighbours is the literal same value, every region reaches the work area's far edges, and the five areas sum to exactly the total — the one check that catches both an overlap and a gap an edge-only check could miss), every workspace **never produces a negative size** even at a pathological 64×64, and the Performance/Viewing workspaces' specific shapes. See `Docs/Documentation/EditorLayout.md`. |
| `Editing/CommandHistoryTests.cs` | `Editing/Undo/CommandHistory.cs` | The undo/redo stack against a spy `ICommand`: `Undo`/`Redo` call the right side of the command (never both), `Push` never calls either (the edit is assumed already applied), undoing multiple commands unwinds in reverse order, undoing past the bottom / redoing with nothing undone are no-ops rather than throwing, a fresh `Push` after an `Undo` **clears the redo stack**, `CanUndo`/`CanRedo` track state through a full undo→redo cycle, `Clear` drops both stacks, and the 200-command capacity evicts the oldest entry rather than growing unbounded. |
| `Editing/TransformCommandTests.cs` | `Editing/Undo/TransformCommand.cs` | A real (GL-free) `World.Transform`: `Undo`/`Redo` restore the exact before/after `TransformState`, and — the one this suite exists to pin — `Undo` goes through `Transform.SetRotation` (not the raw `Rotation` setter), so the `EulerAngles` cache the inspector reads from stays coherent with whatever orientation the undo actually reverted to. See `Docs/Documentation/Undo.md`. |
| `Editing/CompositeCommandTests.cs` | `Editing/Undo/CompositeCommand.cs` | The multi-select bulk-edit path: `Undo` runs every wrapped command's `Undo` in **reverse** order, `Redo` runs every wrapped command's `Redo` in original order, and `CommandHistory.PushRange` — zero commands pushes nothing, exactly one pushes it directly (not wrapped, so a single-entity edit still undoes in one `Undo()` call same as always), and more than one wraps them so the whole group is one undo step. |
| `World/SceneSelectionTests.cs` | `World/Scene.cs` | Multi-select: `Select` replaces the whole set, `ToggleSelect` adds/removes without disturbing the rest of the selection, `Selected` (the "primary") is always the most-recently-added entity — not just whichever one a `HashSet` happens to enumerate first — `RemoveEntity` drops a deleted entity from the selection without disturbing the rest of it, and re-derives a new primary if the one removed *was* the primary. See `Docs/Documentation/Multiselect.md`. |
| `Graphics/BlockCompressionTests.cs` | `Graphics/Resources/BlockCompression.cs` | The BC1/BC3 software texture-compression encoder: alpha detection, BC1 vs. BC3 block-size selection, a solid-color block decoding back within a few 8-bit levels of the source color (565 quantization is lossy by design), BC3's two stored alpha endpoints (block max/min) surviving exactly, and the mip chain's level count + per-level byte size for a regular, a non-power-of-two, and a 1x1 image. Decoding is a second, independent from-spec BC1 reimplementation in the test file itself, not a call back into the encoder's own private helpers — a failure here means the *encoded bytes* are wrong, not just self-consistent. See `Docs/Documentation/TextureCompression.md`. |
| `Simulation/RigidBodyShapeTests.cs` | `Simulation/Physics/RigidBody.cs`, `Simulation/Physics/PhysicsSystem.cs` | Pure-math/pure-logic pieces of the capsule/kinematic/mesh-collider expansion that don't need a BEPU `Simulation` or GL context: `RigidBody.CapsuleDimensions` — radius is the larger of the X/Z half-extents (not just X, a case this suite's own deliberate-breakage spot-check caught), length is whatever's left of the Y half-extent after the radius, and it never goes negative for a squat/wide input; `PhysicsSystem.AngularVelocityFromDelta`, the kinematic-body angular-velocity finite-difference formula: zero for no rotation, `2·sin(θ/2)/dt` (not `θ/dt` — an initial test assertion assumed the latter and had to be corrected) for a quarter-turn over one second, and taking the shorter of the two equivalent quaternion paths near a half-turn; and `PhysicsSystem.TrianglesFromMesh` — the interleaved-vertex-buffer-plus-index-buffer-to-`Triangle[]` conversion a `Mesh` collider is built on, checked against a synthetic two-triangle quad (correct triangle count, correct per-index vertex positions, empty indices producing no triangles) rather than a real Assimp-decoded asset, which doesn't belong in a fast per-commit suite. The rest of the capsule/kinematic/friction/mesh-collider work (actual BEPU simulation — falling, resting, pushing, sliding, exact-vs-bounding-box mesh collision) is verified via the standalone-harness pattern and headless capture instead, per `Docs/Documentation/PhysicsEngine.md` §7 — no in-engine integration test for that, consistent with this subsystem's established approach. |

These targets were picked because they're exactly the kind of logic CLAUDE.md's
standalone-harness note calls out (pure C#, no GL, no window) *and* sit behind a real past or
easily-reachable bug: the `MaterialRegistry` suite exercises the exact merge/prefix machinery
behind this repo's own `uvScale`-stopped-round-tripping regression and the `extends`/`path`
features that followed it; `Transform`'s cycle guard and dirty-flag cache are the foundation the
whole entity-hierarchy feature sits on, with the highest blast radius of anything covered so far
if either ever regressed silently; the `TransformGizmo` projection round-trip guards the exact
sign/axis-flip class of bug that would silently desync where handles draw from where clicks land. Every suite in this table was spot-checked by deliberately
breaking the logic it covers and confirming the expected tests go red (see each's own commit
message for exactly what was broken and which tests caught it) before being trusted.

## 3. Project structure and conventions

- **One test file per source file**, same relative path under `Centauri.Tests/` as the source has
  under `Centauri/` (`Rendering/Shadows/CascadeBuilder.cs` → `Centauri.Tests/Shadows/
  CascadeBuilderTests.cs` — the `Rendering/` prefix is dropped since `Centauri.Tests`'s own root
  already disambiguates it from the source tree). Makes "is there a test file for this" a
  find-by-name question.
- **`ProjectReference`, not a rebuilt-`.dll`-plus-`HintPath`** (`Centauri.Tests.csproj` references
  `../Centauri/Centauri.csproj` directly) — this is the one place the standalone-harness pattern
  in CLAUDE.md is intentionally *not* followed: a throwaway harness references the already-built
  `Centauri.dll` because it's meant to be deleted after one use, but a permanent test project
  should always test against a fresh build, and `dotnet test`/`dotnet build` already handle
  building the referenced project first.
- **`InternalsVisibleTo`** (`Centauri.csproj`) grants `Centauri.Tests` access to `internal` types —
  `EntityHierarchyWiring`, `TrackedEntitySet`, `MaterialRegistry`, `ModelRegistry`,
  `ShadowCasterRenderer` and friends are `internal` on purpose (implementation details of their
  owning subsystem, not part of the engine's public surface), and this lets them stay that way
  instead of forcing a choice between "make it public just to test it" and "don't test it."
- **Naming**: `MethodUnderTest_Scenario_ExpectedBehavior` (e.g.
  `Build_ReusesArrayInstanceWhenLengthMatches`, `Wire_IgnoresUnresolvableParentNameWithoutThrowing`)
  — the test name alone should tell you what broke from a red run without opening the file.
- **Comments explain *why* a case is worth testing**, not what the assertion does — same house
  style as the rest of the codebase (see CLAUDE.md's commenting guidance). A test with no comment
  is one whose scenario is self-evident from its name.

## 4. Adding a new test

1. **Pick a target that's pure-CPU logic** — no `GL`/`GLShader`/`GLTexture`/render-target
   parameters, nothing that needs a window or a GL context to construct. Good signs: the class
   only touches `System.Numerics`, plain data (`AppConfig`, `EntityDefinition`), or other
   already-pure classes. `Camera`, `Transform`, `Entity` (constructed with `model: null`),
   `BoundingBox`, and any `Config/Settings/*.cs` class are all safe to instantiate directly in a
   test — none of them touch GL.
2. **If the target is `internal`**, that's fine — `InternalsVisibleTo` already covers it. Don't
   make something `public` just to reach it from a test.
3. **Write the smallest real object graph that exercises the behavior** — see
   `CascadeBuilderTests.MakeCamera`/`SceneBounds` or `EntityHierarchyWiringTests.Node` for the
   pattern: a small private helper that builds just enough of the surrounding graph, reused across
   the file's `[Fact]`/`[Theory]` methods.
4. **Verify the test can actually fail** before trusting it — temporarily break the logic it
   covers (comment out the line under test, flip a condition), confirm the expected test(s) go
   red, then revert. This project's own two suites were built this way (see their commit message
   for exactly what was broken and which tests caught it) — a test that's never been seen to fail
   is unverified, not passing.
5. **Run the full suite, not just the new test**, before considering it done —
   `dotnet test Centauri.Tests/Centauri.Tests.csproj -c Release`.

### What doesn't fit here (yet)

Anything that needs an actual GL context (shader compilation, texture decode + upload, a full
render) isn't covered by this project and shouldn't be forced into it — `Centauri.Tests` has no
window/Xvfb setup. For that, the existing headless pattern still applies: `CENTAURI_HEADLESS_FRAMES`
+ `CENTAURI_SCREENSHOT_PATH` + `xvfb-run` (see CLAUDE.md's "Headless / CI rendering" section) run
the real engine and produce a screenshot to inspect. §5's CI workflow now runs that path
automatically as a boot/render *smoke test* (crash/exception/shader-failure detection, one
screenshot uploaded for inspection) — but it's still not something `dotnet test` drives, and it
doesn't assert anything about the screenshot's actual pixel content, just that rendering completed
and produced one. Turning specific render-output checks (not just "did it crash") into real,
`dotnet test`-driven assertions is future work, not started here.

`MaterialRegistry`'s file-based tests (`extends` merge, texture-path prefixing) show the pattern
for anything else that needs real files on disk but no GL context: `Directory.CreateTempSubdirectory()`
+ `File.WriteAllText` in the test class, `Directory.Delete(recursive: true)` in `Dispose` (xunit
disposes a non-static test class instance after every test method, so this cleans up per-test, not
just per-suite).

## 5. CI

`.github/workflows/ci.yml` runs on every push and pull request targeting `main`, as two
independent jobs (so a failure in one doesn't hide a result from the other):

- **`build-and-test`** — `dotnet restore`/`build`/`test`, exactly §1's commands. Plain
  `ubuntu-latest` runner, no display needed — this is the same "no GL context" property that
  makes everything in §2's table fast and reliable to begin with.
- **`headless-render-smoke-test`** — installs Xvfb + Mesa's llvmpipe software OpenGL driver
  (`libgl1-mesa-dri`/`libglx-mesa0`, the same combination CLAUDE.md's "Headless / CI rendering"
  section documents and this session used for every manual render verification), then boots the
  engine against the repo's actual checked-in `Config/config.json` — the real default environment
  and entity sets, not a throwaway scene — for a handful of frames via `CENTAURI_HEADLESS_FRAMES`
  and saves a screenshot (`CENTAURI_SCREENSHOT_PATH`), uploaded as a build artifact. A crash, an
  unhandled exception, or a shader compile failure fails this job; a missing/empty screenshot file
  fails it too. This depends on the repo's actual `Assets/` content (skybox HDRIs etc.) being
  present in the checkout CI runs against — a sandboxed dev environment that ships a trimmed
  `Assets/` (see CLAUDE.md's own note on this) isn't representative of what CI itself sees.

Both jobs' exact commands were verified locally before being committed to the workflow — the
`dotnet restore`/`build`/`test` sequence runs identically to how CI invokes it, and the
`xvfb-run … dotnet run` + screenshot-existence check was verified end-to-end against a minimal
scene (this sandbox's own trimmed `Assets/` can't boot the repo's real default config — see the
job description above). What wasn't verified end-to-end here, specifically because of that
trimmed-`Assets/` sandbox limitation, is the smoke-test job booting the *actual* default
config/environment against the real, full `Assets/` tree — worth watching on the first real CI
run after this lands.

## 6. Known limitations / next steps

- **Coverage is a seed, not a target.** Four suites exist because they were the highest-value
  pure-logic pieces identified so far (two picked for being self-contained and GL-free, two more
  picked for sitting behind a real past regression or having the highest blast radius in the
  entity/hierarchy system) — not because everything else is already covered. Other strong
  candidates not yet done: `ModelRegistry` (mirrors `MaterialRegistry`'s duplicate-id detection),
  `ShadowCache.CanReuse`/`CanReuseStaleFit` (the temporal-reuse logic `CascadeBuilder`'s tests
  don't touch), and `PhysicsSystem`'s already-manually-verified cases from
  `Docs/Documentation/PhysicsEngine.md` §6 (BEPU is pure-managed, no GL, so these are portable to
  `Centauri.Tests` too — just heavier to set up than the four suites here).
- **No real render-output testing.** §5's CI smoke test catches "does it still boot and produce a
  frame" (crash/exception/shader-failure), but asserts nothing about the frame's actual content —
  see §4's "what doesn't fit here." This is a real gap (the renderer is the largest,
  most actively-changing part of the engine, per `ENGINE_ROADMAP.md`'s own line-count survey),
  just not one this project's current shape is meant to close.
