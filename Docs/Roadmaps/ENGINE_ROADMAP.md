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
commit history spans about a week — this is a young, fast-moving project, not a mature one with
accumulated cruft; the imbalance below is "where effort actually went first," not decay.

**The renderer is the engine.** Forward+ prepass, CSM + PCSS + spot-light shadows, SSR, planar
reflections, reflection probes, GTAO, TAA, procedural sky/clouds/IBL, bloom, auto-exposure, POM
with self-shadowing, triplanar projection, wind, and a working Tracy + GPU-timer profiling
pipeline — all in ~45% of the codebase, most of it well-organized (small, single-purpose pass
classes; the shadow-mapper duplication that did creep in was just deduped). This is genuinely
ahead of where a week-old solo engine "should" be.

**Everything content-facing is thin by comparison.** `World/Components/Component.cs` defines a
real per-entity behavior extensibility point — and `RigidBody` is its only concrete subclass.
Physics colliders are box/sphere only, no kinematic bodies, no per-material friction/restitution.
There's no audio subsystem, no skeletal animation, no terrain, no water, no LOD/impostor system,
no texture streaming or compression (every texture decodes to full-resolution RGBA8/RGB16F on
load — see `GLTexture.Decode`). The editor has no viewport gizmos (no ImGuizmo or equivalent —
transform editing is drag-a-number-in-the-inspector only), no multi-select, no undo/redo, no
drag-and-drop asset placement. `EntitySetLoader.Save()` doesn't round-trip material property
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

### Phase 0 — Safety net (do first; everything after this compounds on it)

Right now correctness is verified by eyeballing headless screenshots and hoping nothing silently
regressed. That doesn't scale past one person's attention, and it's the one gap that makes every
other phase riskier than it needs to be.

- [ ] **Automated tests.** A real xunit/nunit project, formalizing the throwaway
  standalone-console-project pattern already used ad hoc this session (`CascadeBuilder` math,
  `Model.Decode()`) into something that runs on every change instead of only when someone
  remembers to write a scratch harness. Start with pure-CPU logic that needs no GL context —
  cascade fitting, material `extends` merge, hierarchy wiring, UV/path resolution — before
  reaching for anything render-output-based.
- [ ] **CI.** Build + run the test suite on every push. `HeadlessCapture` + Xvfb/llvmpipe already
  proves the engine can boot and render without a display — wiring that into CI as a "does it
  still boot and produce a frame" smoke test is a small extension of infrastructure that already
  exists, not new invention.

**Exit criteria:** a broken build or an obviously-wrong render (crash, black frame, shader
compile failure) is caught by CI before it's caught by a person.

### Phase 1 — Editor usability (unlocks using the engine for anything beyond this session)

The renderer has no shortage of knobs; almost none of them are reachable without either editing
JSON by hand or dragging a number field to the value you want. This is the actual ceiling on
"can someone build a scene in this," independent of how good the renderer itself is.

- [ ] **Viewport gizmos** (translate/rotate/scale handles — ImGuizmo is the standard pairing with
  Dear ImGui and this project already depends on `Silk.NET.OpenGL.Extensions.ImGui`). Numeric
  drag rows don't substitute for this past trivial placements.
- [ ] **Undo/redo.** Currently the only "undo" is `EntitySetLoader.Reset()` — discard every live
  edit and reload from disk. A real undo stack (even a coarse one — snapshot/diff per edit
  gesture) is table stakes for an editor.
- [ ] **Multi-select** in the Outliner, at least for bulk transform edits and delete.
- [ ] **Persist what's currently live-only:** material property overrides (Color/Roughness/
  Metallic/Translucency/UV — see `EntityInspectorSection`'s own comments on this gap),
  camera/skybox edits (`EnvironmentLoader` has no `Save()` at all right now).

**Exit criteria:** a scene can be built and iterated on entirely through the editor, and every
edit made through the editor survives a save/reload.

### Phase 2 — Content-scale readiness

Everything so far assumes small hand-placed scenes (the `TestScene.json` scale). None of it holds
up once content count grows.

- [ ] **Texture compression / a real texture budget story.** Every texture is full-resolution
  RGBA8 in VRAM the moment its material loads (see `GLTexture.Decode` — no mip-clamping, no
  streaming, no BC/KTX compressed formats). This was already flagged as a real gap in this
  session's texture-resolution discussion; it's worth fixing before content count makes it a hard
  VRAM ceiling instead of a theoretical one.
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
