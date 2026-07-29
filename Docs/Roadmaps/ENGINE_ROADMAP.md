# Engine State & Roadmap

A point-in-time assessment of where Centauri Engine actually stands, and a phased roadmap out of
it — not a wishlist (see `Docs/TODO.md` for that), a *sequenced* plan that tries to fix the
highest-leverage gap first. Methodology: subsystem line-count survey (`Centauri/*`), a feature
audit against what a small-but-real content pipeline needs (editor UX, persistence, physics,
audio, animation, streaming), and a check for testing/CI infrastructure. Written from the state of
the repo after the physics, local-light-shadow, GL 4.3 context-bump, and asset-organization passes
— see git log for exact provenance.

## Where the engine stands today

| Area | Lines (`*.cs`) | Share |
|---|---|---|
| `Rendering/` | ~6,900 | ~45% |
| `UI/` | ~2,800 | ~18% |
| `Graphics/` | ~1,500 | ~10% |
| `Loading/` | ~1,050 | ~7% |
| `World/` | ~890 | ~6% |
| `Config/` | ~710 | ~5% |
| `Simulation/` (physics) | ~540 | ~3% |
| `Utils/` | ~460 | ~3% |
| `Input/` | ~320 | ~2% |

Plus 39 shader files. Zero test projects. Zero CI (`.github/` doesn't exist). The whole repo's
commit history spans about 8 weeks — this is a young, fast-moving project, not a mature one with
accumulated cruft; the imbalance below is "where effort actually went first," not decay.

**The renderer is the engine.** Forward+ prepass, CSM + PCSS + spot-light shadows, SSR, planar
reflections, reflection probes, GTAO, TAA, procedural sky/clouds/IBL, bloom, auto-exposure, POM
with self-shadowing, triplanar projection, wind, and a working Tracy + GPU-timer profiling
pipeline — all in ~45% of the codebase, most of it well-organized (small, single-purpose pass
classes; the shadow-mapper duplication that did creep in was just deduped). This is genuinely
ahead of where an 8-week-old solo engine "should" be.

**Everything content-facing is thin by comparison.** `World/Components/Component.cs` defines a
real per-entity behavior extensibility point — and `RigidBody` is its only concrete subclass.
Physics colliders are box/sphere only, no kinematic bodies, no per-material friction/restitution.
There's no audio subsystem, no skeletal animation, no terrain, no water, no LOD/impostor system,
no texture streaming or compression (every texture decodes to full-resolution RGBA8/RGB16F on
load — see `GLTexture.Decode`). The editor has viewport gizmos (translate/rotate/scale, W/E/R —
self-implemented; see Phase 1 below and `Docs/Documentation/Gizmos.md`) but no multi-select, no
undo/redo, no drag-and-drop asset placement. `EntitySetLoader.Save()` doesn't round-trip material property
overrides or camera/skybox edits (documented in-code, not a bug — just unbuilt).

None of this is a surprise for the project's age. It's exactly the shape you'd expect from
"build the rendering core first" — but it means the *next* phase of highest-leverage work isn't
another rendering feature, it's the infrastructure and editor-usability layer that everything else
will depend on.

## What's already trigger-gated elsewhere (not duplicated here)

- **GL 3.3 → 4.3 feature adoption** (SSBOs, compute, cube-map arrays, multi-draw-indirect) —
  `Docs/Roadmaps/GL4_UPGRADE.md`. Context bump done; each feature has its own trigger condition.
- **POM → tessellation** — `Docs/Roadmaps/DISPLACEMENT.md`. Phase 1 (validate against real
  content/hardware) is the only unstarted, unblocked item there.
- **Point-light shadows** — gated behind the GL4 cube-map-array row above; see
  `Docs/Documentation/LocalShadows.md` §3.

## Roadmap

Phased by dependency and leverage, not by "what's easy." Each phase's exit criteria gate moving to
the next — later phases assume earlier ones landed, same discipline `DISPLACEMENT.md` and
`GL4_UPGRADE.md` already use (don't start a row speculatively; start it when its trigger is real).

### Phase 0 — Safety net (do first; everything after this compounds on it) — complete

Before this phase, correctness was verified by eyeballing headless screenshots and hoping nothing
silently regressed — the one gap that made every other phase riskier than it needed to be. Both
rows below are now done; see each for what's still open within them (more test coverage, watching
the first real CI run against the full `Assets/` tree).

- [x] **Automated tests — landed.** `Centauri.Tests` (xunit, `ProjectReference` to
  `Centauri.csproj`, `InternalsVisibleTo` for internal-only algorithms) formalizes the throwaway
  standalone-console-project pattern this session used ad hoc into something that runs on every
  change instead of only when someone remembers to write a scratch harness. Four suites so far —
  `CascadeBuilder`, `EntityHierarchyWiring`, `MaterialRegistry` (`extends` merge, `path`
  texture-prefixing), `Transform` (re-parent cycle guard, `WorldMatrix` dirty-flag cache) — all
  pure-CPU, no GL context needed, each spot-checked against a deliberately broken version of the
  logic it covers to confirm it actually fails, not just passes vacuously. See
  `Docs/Documentation/Testing.md` for how to run/extend it and what's still open (`ModelRegistry`,
  `ShadowCache`, `PhysicsSystem`'s already-manually-verified cases).
- [x] **CI — landed.** `.github/workflows/ci.yml`, two jobs on every push/PR to `main`:
  `build-and-test` (`dotnet build`/`test`, plain runner, no display needed) and
  `headless-render-smoke-test` (Xvfb + Mesa llvmpipe, boots the real checked-in default
  config/environment for a few frames, uploads a screenshot, fails on crash/exception/shader
  failure or a missing screenshot) — the exact `CENTAURI_HEADLESS_FRAMES`/`CENTAURI_SCREENSHOT_PATH`
  pattern CLAUDE.md's "Headless / CI rendering" section already documents, now automated instead
  of manual-per-change. See `Docs/Documentation/Testing.md` §5 for what was and wasn't verified
  end-to-end before this landed (the smoke-test job's dependency on the repo's real `Assets/` tree
  couldn't be dry-run in this session's own trimmed sandbox checkout — worth watching on the first
  real run).

**Exit criteria — met.** A broken build, a failing test, or an obviously-wrong render (crash,
black frame, shader compile failure) is now caught by CI before it's caught by a person.

### Phase 1 — Editor usability (unlocks using the engine for anything beyond this session)

The renderer has no shortage of knobs; almost none of them are reachable without either editing
JSON by hand or dragging a number field to the value you want. This is the actual ceiling on
"can someone build a scene in this," independent of how good the renderer itself is.

- [x] **Viewport gizmos** (translate/rotate/scale handles) — **done**. `UI/Gizmos/TransformGizmo.cs`,
  all three modes switched with W/E/R, self-implemented (screen-space overlay via ImGui's foreground
  draw list + our own projection/hit-test/drag on the existing camera math) rather than ImGuizmo, to
  avoid adding a per-RID native binary the 4-way CI matrix + headless job would all need; see
  `Docs/Documentation/Gizmos.md` §1 for the rationale and §4 for optional extensions
  (local/world toggle, snapping, plane/uniform handles). One caveat: the rotate/scale drag *feel* is
  math-and-test-backed but couldn't be exercised without a cursor headless — worth an interactive
  sanity check.
- [x] **Docked, resolution-independent editor layout** — **done**. `UI/Layout/EditorLayout.cs` +
  three Blender-style workspaces (Edit / Performance / Viewing) switched from `TopBar`, replacing
  the earlier floating-card panels — no panel positions itself or can overlap/gap another; the
  performance graphs are their own panel (`PerformancePanel`) so they get real width instead of a
  350px card; the gizmo mode bar moved to a left tool column. See
  `Docs/Documentation/EditorLayout.md`.
- [x] **Undo/redo** — **first pass done, since extended to every inspector edit path**.
  `Editing/Undo/` — a Command-pattern `CommandHistory` (Ctrl+Z/Ctrl+Y), coarse/gesture-level as this
  row itself invited: one step per completed gizmo drag or inspector Transform-section gesture
  (`TransformCommand`), entity create/delete (`CreateEntityCommand`/`DeleteEntityCommand`), a
  completed Material or RigidBody-Mass field edit (`FieldEditCommand<T>`, via new optional
  `CommandHistory` tracking in `Widgets`' row helpers), a RigidBody Kind/Shape attach/detach/change
  (`RigidBodyCommand`), or a Hierarchy-section re-parent (`ReparentCommand`) — not a per-frame or
  per-keystroke diff. See `Docs/Documentation/Undo.md` for what's covered, the one remaining
  deliberately-deferred item (a deleted entity's former children aren't re-linked to it on undo),
  and two known limitations: object identity across a delete→undo→further-undo sequence, and a rare
  stale-tracking-key edge case in the field-edit mechanism.
- [x] **Multi-select** — **done**. `Scene.SelectedEntities` (Outliner Ctrl/Shift-click, viewport
  Ctrl+click) — bulk gizmo drag (every selected entity moves/rotates/scales by the same delta) and
  bulk delete, both as a single undo step via the new `Editing/Undo/CompositeCommand`. See
  `Docs/Documentation/Multiselect.md` for what's covered and what's deliberately out of scope
  (multi-entity property editing, true shared-pivot group transforms, box-select).
- [x] **Persist what's currently live-only** — **done**. Material property overrides
  (Color/Roughness/Metallic/Translucency/UV/etc.) now round-trip via a new per-slot
  `MaterialOverride` snapshot, written by `EntityDefinitionWriter` for any slot
  `Entity.OwnsMaterial` (clone-on-write via the pre-existing `MakeMaterialUnique`) and reapplied by
  `EntityFactory.ResolveMaterials` on load; see `Docs/Documentation/MaterialPersistence.md`.
  `EnvironmentLoader` gained a `Save()` (cameras, skybox exposure/black-level, the optional sun),
  wired into the same Ctrl+S as entity-set saves; see `Docs/Documentation/EnvironmentPersistence.md`
  for what's covered and what's deliberately deferred (no symmetric `Reset()` for the environment
  half, no dirty-tracking). Material-override persistence is code-reviewed and build-verified but
  not live-verified in this sandbox (no model assets present to test against); environment
  persistence was live-verified headless end to end.

**Exit criteria:** a scene can be built and iterated on entirely through the editor, and every
edit made through the editor survives a save/reload.

### Phase 2 — Content-scale readiness

Everything so far assumes small hand-placed scenes (the `TestScene.json` scale). None of it holds
up once content count grows.

- [x] **Texture compression / a real texture budget story** — **done**. Every LDR texture now
  uploads as BC1 (opaque, ~6:1) or BC3 (alpha, ~4:1) instead of raw RGBA8, via a hand-written
  `Graphics/Resources/BlockCompression.cs` encoder (no new dependency — S3TC is simple enough not
  to need one, matching the project's existing native-binary aversion) with its own manually
  box-filtered mip chain (GPU `glGenerateMipmap` doesn't work on compressed formats). Falls back to
  the original uncompressed path automatically per-texture (HDR, unsupported driver, or below one
  4x4 block) — `GL_EXT_texture_compression_s3tc` confirmed present even on this sandbox's headless
  Mesa/llvmpipe CI target, so the compressed path is exercised for real by the existing smoke-test
  job, not dormant. `StatsOverlay` gained a "Textures" section (compressed/uncompressed counts,
  estimated VRAM) for budget visibility. See `Docs/Documentation/TextureCompression.md` for the
  full writeup, including what's deliberately still out of scope: no refcounted cache eviction
  (`TextureCacheSize` remains unused — evicting without knowing whether a live `Material` still
  references a texture risks a use-after-free; needs its own design pass first, same as LOD below),
  no mip-clamping/max-resolution cap, no BC7/ASTC, no pre-compressed asset (KTX2/DDS) import.
- [ ] **LOD / impostors** (TODO.md item, currently unstarted, no design doc yet). Needs its own
  short design pass before implementation — impostor billboarking interacts with the shadow
  casters and instancing path in ways worth thinking through up front.
- [ ] **Physics: more collider shapes (capsule at minimum, mesh colliders for statics), kinematic
  bodies, per-material friction/restitution.** The current box/sphere-only, dynamic/static-only
  scope was intentionally minimal for a first pass (see `Docs/Documentation/PhysicsEngine.md`);
  this is the natural next slice, not new scope.

**Exit criteria:** a scene with meaningfully more content (tens-to-hundreds of instances, several
texture sets) doesn't degrade VRAM or physics behavior in ways that require manual workarounds.

### Phase 3 — Feature breadth (catching the rest of the engine up to the renderer's ambition)

These are real, substantial subsystems — each deserves its own design pass (mirroring how physics
and local-light shadows got their own `Docs/Documentation/*.md` this session) before
implementation starts. Listed in rough dependency/leverage order, not commitment order.

- [ ] **Audio.** Nothing exists yet — no dependency, no playback, no spatialization. Lowest
  technical risk of this group (doesn't interact with the render graph), so a reasonable first
  pick if this phase starts before Phase 2 fully lands.
- [ ] **Skeletal animation / skinning.** Assimp (already a dependency via `Silk.NET.Assimp`) can
  supply bone data; `Model.Decode()` doesn't currently read or store it. Real scope: skinning
  matrices in the vertex shader, an animation-clip playback system, a `Component` for driving it
  (the first real second consumer of the `Component` extensibility point besides `RigidBody`).
- [ ] **Terrain.** Technique still genuinely undecided (TODO.md flags this explicitly, pointing at
  diffusion-based generation as one option) — needs a research/prototype spike before a real
  design doc is possible, unlike the other items here.
- [ ] **Water.** TODO.md's own note already sketches the approach (planar as base + SSR
  contact reflections, `mix(planar, ssr, ssrConfidence)`, distortion wave normals) — worth
  doing once there's an actual body of water in a scene to motivate it, not before.

**Exit criteria:** none — this phase is inherently open-ended breadth work. Reassess priority
order once Phase 2 lands and it's clearer which of these a real scene actually needs first.

## What this roadmap deliberately doesn't include

- Anything already covered by `GL4_UPGRADE.md`'s trigger table — don't duplicate that gating logic
  here.
- Raytracing/BVH (TODO.md marks this experimental-only, not real-time; no phase above depends on
  it).
- Sky/cloud quality polish (Hosek-Wilkie, full raymarched volumetrics) — genuine "optional" per
  TODO.md, not blocking anything else.
