# LOD / Impostors — Design

**Status: design only, nothing in this document is implemented yet.** This is the "short design
pass" `Docs/Roadmaps/ENGINE_ROADMAP.md` calls for before starting the Phase 2 LOD/impostor item —
working out how the feature has to fit the existing culling/instancing/shadow architecture *before*
committing code, not a record of something already built. Treat every class/field name below as a
proposal, not an API that exists today.

## 1. Scope: impostors first, mesh-LOD chains deferred

"LOD / impostors" bundles two different techniques the roadmap doesn't distinguish:

- **Mesh LOD** — swap to a lower-poly *hand-authored* mesh of the same object at distance
  (LOD1, LOD2, ...), still a real 3D mesh, still lit normally.
- **Impostors** — swap to a camera-facing textured quad at distance, trading real geometry for a
  flat billboard.

This design covers **impostors only**, and defers mesh-LOD chains to a later, smaller follow-up
(§8). Two reasons:

1. Impostors are the piece the roadmap explicitly flagged as needing a design pass ("interacts
   with the shadow casters and instancing path") — mesh-LOD is architecturally simpler once the
   harder problem (impostors) is solved, since it's the same "what does this entity actually draw
   this frame" indirection with a different outcome (§4).
2. Impostors *consolidate* geometry (every impostor'd entity across every source model can share
   one quad `Model` and draw call — see §5), which is a clean, bounded addition to the render path.
   Mesh-LOD *fragments* it (each source model needs its own LOD1/LOD2 `Model`, so LOD-aware
   batching needs a per-(tier, source-model) bucket, not one shared bucket) — real added
   complexity that's better decided once impostors have proven the tier-selection/hysteresis
   machinery works.

## 2. Why this needs a design pass at all

Three pieces of existing architecture, read closely while researching this doc (all file
references current as of this write-up):

- **`Entity.Model` has no setter** (`World/Entity.cs`) — an entity's geometry is fixed at
  construction. Nothing today changes what mesh an entity draws after it's placed.
- **`ShaderBatcher` keys batches by `Model` *reference identity* plus material array identity**
  (`Rendering/Helper/ShaderBatcher.cs`), rebuilt only when `Scene.Revision` changes — i.e. only on
  scene edits (add/delete/material swap), never on camera movement. An LOD tier is a
  camera-distance-driven signal with no scene-edit component, so it can't drive the same
  revision-gated rebuild without either (a) making camera movement bump `Scene.Revision` — which
  would invalidate every batch and the shadow cache on every frame a tiered entity crosses a
  threshold, not just the tiered one — or (b) keeping tier selection entirely out of
  `ShaderBatcher`'s rebuild and doing it as a downstream filter instead (§4).
- **`ShadowCasterRenderer.BucketCasters`** (`Rendering/Shadows/ShadowCasterRenderer.cs`) does its
  own independent cull + `Model`-identity bucketing against `CullingSystem`'s grid — completely
  decoupled from whatever `MainRenderer` decides to draw for the same entity. Left untouched, an
  entity that's impostor'd in the forward pass would still cast a full-mesh shadow (fine,
  arguably) or — if the impostor swap *did* propagate here uncritically — cast a shadow shaped
  like a flat card rotated to face wherever the camera happened to be, which would swing visibly
  as the camera orbits. Neither is acceptable without an explicit decision (§6).

None of this is exotic to fix, but all three need one consistent answer, decided once, rather than
worked out ad hoc mid-implementation — hence the design pass.

## 3. The component: `LodComponent`

Follows the one existing precedent for a concrete `Component`, `Simulation/Physics/RigidBody.cs`:
a thin, engine-agnostic config-and-live-state bag, owned/interpreted by a separate system, not a
place where rendering logic lives.

```csharp
public sealed class LodComponent : Component
{
    // Config — authored per entity (or per-model default, mirroring ModelDefinition's existing
    // TriplanarOverride pattern, so a whole asset type doesn't need re-authoring per placement).
    public float ImpostorScreenSize = 0.05f;  // switch-to-impostor threshold (§7)
    public float HysteresisFactor   = 0.85f;  // switch-back-to-mesh threshold = ImpostorScreenSize * this (§7)
    public string? ImpostorMaterial;          // .mat asset for the billboard quad (§5)

    // Live state — written by the tier query (§4), read by MainRenderer/ShadowCasterRenderer.
    // Same shape as RigidBody's LinearVelocity/AngularVelocity: derived output, not something
    // gameplay/editor code sets directly.
    internal LodTier CurrentTier = LodTier.Mesh;
}

public enum LodTier { Mesh, Impostor }
```

An entity with no `LodComponent` behaves exactly as today — the whole feature is additive and
opt-in per entity, same as `RigidBody`.

## 4. Where the tier decision happens: a stateless query, not an owning system

`RigidBody` is owned by `PhysicsSystem`, which ticks every registered body once per fixed step —
that shape fits physics because a body has persistent simulation state to integrate. LOD doesn't:
there's nothing to integrate, only a per-frame classification of "how big is this on screen right
now." Giving it a full owning system with its own `Update(dt)` pass over every `LodComponent`-
bearing entity would also mean iterating entities the current frame never even draws (off-screen
ones) — wasted work `CullingSystem` has already ruled out by the time it would run.

Instead: a stateless(-ish) query function, called *inline* wherever an already-visible entity is
about to be routed to a draw call:

```csharp
public static class LodEvaluator
{
    // Called from MainRenderer.CollectVisibleInstances and ShadowCasterRenderer.BucketCasters,
    // only for entities CullingSystem has already confirmed are visible — never a whole-scene
    // pass. Reads + updates entity.GetComponent<LodComponent>()!.CurrentTier in place (the one
    // piece of real state this owns) and returns the tier for the caller to route on.
    public static LodTier Evaluate(Entity entity, Camera camera);
}
```

"Stateless-ish": no owning system, no per-frame enumeration of its own — but `CurrentTier` is real,
persistent state living on the component (same pattern as `RigidBody.LinearVelocity`), because
hysteresis (§7) needs to know which tier the entity was in *last* time it was evaluated, not just
its instantaneous screen size.

## 5. The impostor quad and shader

**A single shared quad `Model`**, built the same way `SkyboxRenderer.BuildCube()`/`GridRenderer`
already hand-build procedural geometry (`Graphics/Geometry/Model.cs`'s
`Model(GL gl, IEnumerable<Mesh> meshes)` constructor — the same one those two use, no Assimp
import). Every impostor'd entity across every source model shares this one `Model` — this is what
makes impostors cheap to batch: they all key to the *same* `Model` reference regardless of what
they used to be, so they consolidate into very few draw calls rather than fragmenting per source
asset (unlike mesh-LOD, §1).

**A new dedicated shader** (`Shaders/Impostor/impostor.vert`/`.frag`), not `shaderPBR.frag`.
Grepping every existing `.vert` under `Shaders/` found no camera-facing/billboard technique
anywhere in this codebase today (`SkyboxRenderer`/`CloudPass`'s cube is camera-*translated*, not
camera-*rotated*; `GridRenderer`'s quad is a static world-space plane) — this is new territory, not
an adaptation of something that exists.

**Cylindrical billboarding for v1**, not full spherical: lock the billboard's "up" axis to world Y
and only rotate around Y to face the camera's horizontal bearing. This is the standard choice for
vegetation (the obvious first real use case, given `Testing/Trees/Tree.glb` is the one real model
asset already in this repo) and avoids the classic impostor artifact of a distant tree visibly
tilting/leaning as the camera's pitch changes. Full spherical billboarding (also rotates to face
vertically) is a small per-shader-variant addition later if a non-vertical use case shows up (a
distant rock, a distant character) — not built until something actually needs it.

**Rides the existing instancing pipeline mechanically.** An impostor's `InstanceData` is still just
a world matrix (`Graphics/Geometry/Instancing.cs`) — position and scale come through as normal, the
impostor vertex shader just discards the matrix's rotation and re-derives camera-facing orientation
from `uView` per-vertex instead. No new instance-buffer machinery, just a second draw call (the
impostor batch, §6) using the existing `InstanceBuffer`.

**Impostor art is pre-authored, not runtime-baked, for v1.** An entity's `ImpostorMaterial` is a
plain `.mat` reference — the *existing* material/texture pipeline handles it entirely, zero new
infrastructure. This trades flexibility (someone has to author or generate the billboard texture
per asset up front) for zero implementation risk. Runtime octahedral-impostor baking (render the
real mesh from N angles into an atlas once, generate the billboard automatically for any model) is
strictly more powerful but a real chunk of new engineering — offscreen bake pass, atlas packing,
cache invalidation — deferred as its own later trigger (§8), same spirit as
`DISPLACEMENT.md`'s tessellation phase: don't build the general solution before anything concrete
needs it.

## 6. Batching and shadow-caster integration: filter-and-reroute, not a new batch dimension

The core decision from §2's tension: **`ShaderBatcher` stays exactly as it is today.** LOD is
handled entirely as a downstream step in `MainRenderer.CollectVisibleInstances`
(`Rendering/MainRenderer.cs`), not as a new axis in the batch key.

Concretely: `CollectVisibleInstances` already walks each `Batch`'s entities, filtering by
`Enabled`/`CullingSystem.IsVisible`. It gains one more filter — for any entity carrying a
`LodComponent`, call `LodEvaluator.Evaluate`; if the result is `LodTier.Impostor`, don't append it
to that batch's normal `InstanceData` list, append it instead to a single scene-wide
`_impostorInstances` list threaded through every batch's collection pass. After every normal batch
has drawn, one extra pass draws `_impostorInstances` — the shared quad `Model`, the impostor
shader, one (or a handful, if `ImpostorMaterial` varies) draw call covering every impostor'd entity
in the frame regardless of source model.

This means:

- **`Scene.Revision` is never touched by an LOD transition** — it stays a pure scene-edit signal,
  exactly as today. `ShaderBatcher`'s existing revision-gated rebuild, and `ShadowCache`'s
  matrix-based caching (keyed on camera+light, not caster content — confirmed by reading
  `Rendering/Shadows/ShadowCache.cs`), are both unaffected by camera movement crossing an LOD
  threshold.
- **Tier decisions are recomputed every frame**, not cached — cheap, since `LodEvaluator.Evaluate`
  only ever runs over the already-culled visible set, never the whole scene (§4).

**Shadow casters: impostor-tier entities are excluded from shadow casting entirely**, not given a
flat-quad shadow. `ShadowCasterRenderer.BucketCasters` gains the same `LodEvaluator.Evaluate` check
`MainRenderer` uses (against entities its own independent cull already found visible) and simply
skips appending impostor-tier entities to its casters — no impostor shadow pass at all. This is a
deliberate simplification, not an oversight: a flat card's shadow silhouette reads as visibly wrong
compared to the mesh it replaced, and by construction an entity has only become impostor-eligible
once it's small/far enough on screen that its shadow contribution is usually negligible or already
past `ShadowConfig.Distance` (default 150 world units) — the existing CSM far cutoff. Tuning
`ImpostorScreenSize` (§7) so impostor range sits beyond typical shadow range in practice is a
content/config concern, not something this design needs to solve structurally.

## 7. Tier selection metric and hysteresis

**Screen-space projected size, not raw world distance.** A fixed world-unit threshold doesn't mean
the same thing at different fields of view or for objects of different actual size — the metric
that actually correlates with "is the mesh-vs-impostor swap visible to the player" is how large the
entity's silhouette is on screen. Approximate as the world bounding sphere's angular size:

```
projectedSize ≈ (2 * boundsRadius) / (distance * tanHalfFov)
```

using `Entity.GetWorldBounds()` (already computed/cached, `World/Entity.cs`) for the radius and the
same `tanFov` pattern `Camera.cs` (line ~144) already computes from `Camera.Zoom` (the FOV-in-
degrees field, confusingly named) for perspective math — no new camera API needed, just reusing
what's already there. `ImpostorScreenSize` is a fraction of viewport height (default `0.05` — an
entity occupying less than 5% of the screen's vertical extent qualifies for impostor).

**Hysteresis is a correctness requirement, not a polish pass.** Without it, an entity sitting near
the threshold flickers between mesh and impostor every frame from ordinary camera jitter/motion —
this makes the feature unusable, not just visually rough, so it ships with the first version, not
added later. `HysteresisFactor` (default `0.85`) means: switch *to* impostor when projected size
drops below `ImpostorScreenSize`, but only switch *back* to mesh when it rises back above
`ImpostorScreenSize / HysteresisFactor` (i.e. a noticeably larger threshold than the one that
triggered the switch away) — a dead zone between the two thresholds, standard technique, directly
why `LodComponent.CurrentTier` needs to persist across frames rather than being recomputed from
scratch each time (§3/§4).

## 8. Explicitly deferred (not attempted this pass, not blocking it either)

- **Mesh-LOD chains** (§1) — same tier-decision machinery (`LodEvaluator`, hysteresis), but the
  batching side needs a per-(tier, source-model) bucket instead of one shared impostor bucket,
  since a LOD1 mesh isn't shared across different source assets the way the impostor quad is. Pick
  this up once impostors are shipped and the tier/hysteresis plumbing is proven; the shape of that
  bucket is a smaller, separate decision, not made here.
- **Runtime-baked impostors** (§5) — pre-authored `.mat` billboards only, for now. Automatic
  octahedral-impostor baking is a real project on its own (offscreen render-to-atlas pass, packing,
  cache invalidation when a source model's geometry/material changes) — a trigger-gated follow-on
  once several assets actually need impostors and hand-authoring the billboard art becomes the
  bottleneck, mirroring how `DISPLACEMENT.md` gates tessellation behind a measured, not
  anticipated, need.
- **Full spherical billboarding** (§5) — cylindrical (Y-locked) only, until a non-vertical use case
  exists.
- **Impostors casting shadows** (§6) — excluded structurally, not planned as a future addition
  unless a specific scene shows the shadow gap (impostor range entities missing their shadow)
  actually matters visually, which given `ShadowConfig.Distance`'s existing far cutoff seems
  unlikely to be the common case.
- **LOD-aware GPU culling / indirect draws** — out of scope entirely; this design works entirely
  within the existing CPU-side `CullingSystem`/`ShaderBatcher`/`InstanceBuffer` pipeline, no GPU-
  driven culling exists in this engine (GL 3.3 core target) to build on.

## 9. Proposed implementation order (once this design is accepted)

1. `LodComponent` + `LodTier` enum + `ComponentFactory`/`EntitySetLoader` JSON round-trip (mirrors
   how `RigidBody` was wired up — see `Docs/Documentation/PhysicsEngine.md` for that precedent).
2. `LodEvaluator.Evaluate` (pure math — screen-size + hysteresis) as a standalone, pure-C# unit —
   genuinely testable without a GL context, unlike almost everything else LOD touches, the same way
   `BlockCompressionTests` could pin `BlockCompression`'s math directly
   (`Docs/Documentation/TextureCompression.md` §7's testing rationale applies identically here).
3. The shared impostor quad `Model` + `impostor.vert`/`.frag` shader pair, verified in isolation
   (one hand-placed impostor'd entity, headless screenshot) before touching batching at all.
4. `MainRenderer.CollectVisibleInstances`'s filter-and-reroute (§6) + the impostor draw pass.
5. `ShadowCasterRenderer.BucketCasters`'s impostor-exclusion hook (§6).
6. Inspector authoring (an `LodComponent` section in the Properties panel, mirroring
   `EntityPhysicsSection`) + undo support for its fields, following the same
   `Widgets`/`CommandHistory` pattern `Docs/Documentation/Undo.md` §2 already established for
   `EntityPhysicsSection`'s own fields.

Each step is independently headless-verifiable before moving to the next, same discipline every
other feature this session has followed.
