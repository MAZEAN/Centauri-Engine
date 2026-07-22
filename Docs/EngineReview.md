# Centauri Engine — Review: hidden bugs, quality & performance

Reviewed on branch `claude/engine-review-reflections-culling-acdj73`.
`dotnet build Centauri-Engine.sln -c Release` succeeds cleanly (exit 0).

Focus: hidden bugs and higher-value quality/perf improvements, with emphasis on the
reflection stack (SSR / planar / probe) and the culling system. Findings are ordered by
impact; each gives `file:line`, the mechanism, and a suggested fix. *Confidence* notes how
sure I am it's a real defect vs. intended behaviour.

---

## Top findings — correctness & performance

### 1. [High] Physics moves Transforms every frame → full grid **and** batch rebuild every frame
- **Where:** `PhysicsSystem.Interpolate()` (`Simulation/Physics/PhysicsSystem.cs:229`) runs once
  per rendered frame (`Simulation/SimulationSystem.cs:96`) and unconditionally writes
  `t.Position`/`t.Rotation` for every dynamic body.
- **Chain:** `Transform.Position.set` → `MarkDirty` → `MarkWorldDirty` → `OnChanged` →
  `Scene.MarkDirty()` → `Revision++` (`World/Scene.cs:25,40`). Rendering reads `WorldMatrix`
  each frame (clearing `_worldDirty`), so the next `Interpolate` re-fires `OnChanged` — so
  **`Scene.Revision` changes every single frame**.
- **Consequence:** `CullingSystem.Update` rebuilds the whole spatial grid over *all* entities
  (`Culling/CullingSystem.cs:59-63` → `SpatialGrid.Rebuild`), and `ShaderBatcher.GetBatches`
  rebuilds *all* batches with fresh allocations + a sort (`Rendering/Helper/ShaderBatcher.cs:60-99`)
  — every frame, for the entire scene, whenever ≥1 dynamic body exists, no matter how few
  entities actually moved. `Scene.cs:30-36` explicitly assumes *"No currently-shipped Component
  mutates Transform every frame"* — physics violates that documented invariant.
- **Scope:** only bites when `Physics.Enabled` and a dynamic body exists (a static scene is
  unaffected), but any physics scene then pays full rebuild + per-frame GC churn.
- **Confidence:** High (traced end-to-end).
- **Fix:** (a) split `Scene.Revision` into a *structural* revision (add/remove/material/hierarchy)
  and a *transform* revision, and rebuild batches only on the former; (b) update the grid
  incrementally for the moved set instead of a full `Rebuild`; (c) at minimum add the equality
  guard from #6 so *at-rest* bodies stop churning.

### 2. [High] Reflection probe has no parallax (box) correction — `uProbePosition` is a dead uniform
- **Where:** `Shaders/SSR/ssr_resolve.frag:68-81` (`probeFallback`) samples
  `textureLod(uProbeMap, Rworld, …)` with the raw world reflection vector. `uProbeBoxMin/Max`
  are used *only* to fade the probe weight; `uProbePosition` (`ssr_resolve.frag:32`) is declared
  but never read (confirmed by grep — the declaration is its only occurrence).
- **Consequence:** the local cubemap is treated as infinitely far away, so reflected geometry
  doesn't stay anchored to the world as the camera/surface move — the classic "local reflection
  slides" artifact a parallax-corrected probe exists to fix. The uniforms to do it right are
  already plumbed through (`RenderingSystem.GetInputs` builds `ProbeBoxMin/Max/Position`).
- **Confidence:** High.
- **Fix:** intersect `Rworld` with the probe AABB (`uProbeBoxMin/Max`), then sample the cubemap
  along `(hitPoint − uProbePosition)`. ~6 lines in `probeFallback`.

### 3. [Med] `ShaderBatcher` rebuilds on transform-only changes (batches are transform-independent)
- **Where:** `Rendering/Helper/ShaderBatcher.cs:62` gates on `scene.Revision`. Batches depend only
  on model + material identity, never on transforms, yet a transform move bumps Revision (see #1)
  and forces a full rebuild (new dictionaries, `Materials.ToArray()`, sort).
- **Fix:** key the batcher off a structural revision, not the transform-inclusive one (pairs with #1a).

### 4. [Med/Low] Projection is D3D-style `[0,1]`-NDC (System.Numerics) but GL runs without `glClipControl` → ~half the depth buffer unused
- **Where:** `Camera.GetProjectionMatrixRaw` uses `Matrix4x4.CreatePerspectiveFieldOfView`
  (`World/Camera.cs:139`); the code documents "near z = 0, far z = 1"
  (`Camera.cs:98-99`, `Shadows/CascadeBuilder.cs:99`). No `glClipControl`/`glDepthRange` call
  exists anywhere (grep).
- **Consequence:** with GL's default clip volume (`[-1,1]`) + default depth range, window-space
  depth is confined to ~`[0.5, 1.0]` — losing ~1 bit of an already-nonlinear distribution (more
  z-fighting at distance) — and the GL near clip sits *closer* than the intended near plane
  (geometry nearer than `near` isn't clipped). It's all self-consistent (shaders reconstruct with
  the same matrices; `Frustum.cs:19` correctly extracts the near plane in the `[0,1]` form), so
  it's wasted precision rather than a visible bug.
- **Confidence:** High on the mechanism; Medium on how much it matters visually.
- **Fix:** `glClipControl(GL_LOWER_LEFT, GL_ZERO_TO_ONE)` (ARB_clip_control — core in GL 4.5,
  widely available as an extension on 3.3-class GPUs) uses the full range and clips at the true
  near plane; pairs naturally with reverse-Z + float depth for the big precision win. Caveat: the
  engine advertises GL 3.3 core, so this means requiring the extension or bumping the version.

### 5. [Low] `Transform` position/rotation setters have no equality guard
- **Where:** `World/Transform.cs:44-72`. Writing an unchanged value still calls `MarkDirty`.
  Combined with #1, a *sleeping* physics body (interpolated pose bit-identical frame to frame)
  still churns Revision. `if (_position == value) return;` short-circuits the at-rest case.

### 6. [Low, latent] `Entity.Transform` setter re-subscribes the entity's own handler but not the Scene's
- **Where:** `World/Entity.cs:34-44` moves `OnTransformChanged` to the new transform, but
  `Scene.AddEntity` subscribed `MarkDirty` to the *old* one (`World/Scene.cs:40`). Nothing
  reassigns `entity.Transform` today (grep is clean), so it's latent — but the asymmetry means a
  future wholesale Transform swap would silently stop bumping Revision (→ stale culling). Either
  forbid reassignment or re-raise through the scene in the setter.

---

## Reflection stack — further improvements (beyond the bugs above)
- **SSR march is a fixed-step linear screen march** (`Shaders/SSR/ssr.frag`, `marchRay`). A Hi-Z /
  min-depth mip pyramid would let long rays skip empty space and cut the step budget for the same
  quality — the single biggest SSR perf lever. The march is otherwise well built (perspective-correct
  `1/w`, binary refine, thickness-after-refine, thoughtful confidence fades).
- **SSR temporal reprojects camera motion only** (`ssr_temporal.frag`): no per-object velocity, so
  reflections of/on moving objects ghost. TAA already computes a velocity buffer — feed it in.
- **Probe:** no automatic/periodic re-bake (moving objects only reflect after a manual Rebake —
  acknowledged in `Reflections/Probes/ReflectionProbeBaker.cs:24-30`); single global probe (no
  blending/volumes). Parallax (#2) is the highest-value item.
- **Planar:** the "oblique near-plane clip" is actually a *fragment* discard (`shaderPBR.frag:835`),
  not an oblique projection / clip plane — visually correct, but it still runs vertex work, can't
  early-Z reject under-plane geometry, and only the PBR pass honours it (fine today since the planar
  pass draws just the forward pass + skybox). Reflected geometry reuses the main view's shadow
  cascades (`Reflections/Planar/PlanarReflectionsPass.cs:18-20`), so reflected shadows can be wrong
  for surfaces facing away from the main camera — acknowledged.
- **Minor:** `SSRPass._output` is written but never read (dead field, `SSRPass.cs:46,176`);
  `PlanarReflectionPass.ResolvePlaneHeight` scans all entities by name every frame
  (`PlanarReflectionsPass.cs:135`) — cache the reflector lookup.

## Culling — further improvements
- **Cell-fully-inside fast path:** `SpatialGrid.Cull` re-tests every entity AABB even when the cell
  is entirely inside the frustum (`Culling/SpatialGrid.cs:137-139`). Detecting "cell inside all 6
  planes" lets you accept the whole cell without per-entity tests — a standard cheap win in dense
  scenes.
- **Incremental updates for moving entities** instead of a full `Rebuild` (see #1b): re-bucket only
  the moved entities.
- **No distance / small-object culling:** entities are kept purely on frustum intersection; a
  screen-size or distance cutoff would drop tiny/faraway meshes cheaply.
- **Frustum-only, no occlusion culling:** for heavy-overdraw scenes a Hi-Z occlusion pass (which
  pairs with the SSR Hi-Z above) would help; the Z-prepass already lays down the depth it'd need.
- `Frustum.CreatePlane` divides by `normal.Length()` with no zero guard (`Utils/Geometry/Frustum.cs:39-44`)
  — only reachable with a degenerate matrix, so low risk, but a cheap guard avoids NaNs.

---

## Spot-checked and correct (no action needed)
- **SSR ↔ IBL delta math:** `ssr_resolve.frag`'s `(targetSpec − skyboxSpec)·materialAo·gtao` matches
  `shaderPBR.frag`'s `AmbientLighting` specular term exactly (same `FresnelSchlickRoughness`,
  split-sum weight, IBL intensity, and AO/GTAO attenuation), and `prepass.frag:67` writes material
  AO into `.b` as the resolve expects — so the SSR term neither double-counts nor under-subtracts
  the environment reflection.
- **SSR temporal** double-buffering and history invalidation on setting/resolution change
  (`SSRPass.cs:148-181`).
- **CascadeBuilder** shadow stability (fixed-origin light view + texel/Z/radius snapping).
- **Reflection-probe bake** leaves the viewport at probe resolution, but
  `PostProcessor.BeginScene → HDRFramebuffer.Bind` resets it before the scene draws
  (`HDRFramebuffer.cs:38`) — no glitch.
