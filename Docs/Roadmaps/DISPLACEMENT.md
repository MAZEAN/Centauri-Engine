# Displacement Mapping Roadmap

Tracks what's left for the "Displacement mapping" TODO.md item beyond what's already shipped.
See the Notes section there for the one-line pointer back to this file.

## Status snapshot

Parallax occlusion mapping (POM) is implemented in `shaderPBR.frag`:

- Steep-parallax ray march + linear-interpolation refinement (`ParallaxUV`)
- View-angle- and scale-adaptive layer count, 8–64 (`PARALLAX_REFERENCE_SCALE`,
  `PARALLAX_ABSOLUTE_MAX_LAYERS`)
- UV-pole / degenerate-tangent-basis offset clamp (defense in depth, not a full fix — see
  Phase 3, trigger 1)
- Soft self-shadowing against the directional light only (`ParallaxSelfShadow`)
- Per-entity enable toggle (`Material.ParallaxEnabled`, inspector "Displacement" checkbox)
- Global debug view (`ShadingMode.ParallaxDebug` — viewport toolbar / `G` cycle)

No tessellation, no true vertex displacement — silhouettes stay flat. See Phase 3.

## Phase 1 — Validate on real content (do first; cheap, no design work)

- [x] Test self-shadowing against at least one real (photographed) height map — brick,
  cobblestone, or similar with continuous gradients. Everything verified so far used a
  hand-authored hard-edged synthetic checker, chosen to make ray-march bugs easy to spot,
  not to judge how the *effect* actually looks.
- [x] Measure GPU frame cost on real hardware for a scene with several POM materials at the
  64-layer ray march + 16-layer self-shadow march. Every measurement to date is from
  headless llvmpipe (software rasterizer) at 4–7 FPS baseline — informative for
  correctness, meaningless for cost.
- [x] Tune `parallaxScale` defaults (currently 0.05) and `PARALLAX_REFERENCE_SCALE` (0.02)
  against 2–3 real project assets instead of synthetic textures — both are first-guess
  values, never validated against real content.

**Exit criteria:** acceptable visual quality and frame cost confirmed on real assets/hardware.
A failure here becomes the next concrete work item (retune constants, or feed the cost finding
into Phase 2's layer-count decision) — it does not by itself justify jumping to Phase 3.

## Phase 2 — Polish within POM (do only if Phase 1 finds a gap)

- [ ] Point/spot self-shadowing — only if a scene actually depends on a point/spot light (not
  the directional sun/sky) for the primary reveal of a displaced surface. Skip by default;
  cost multiplies per light in range.
- [x] Adaptive layer count from screen-space UV derivatives — done ahead of a measured Phase 1
  gap (cheap, no design risk, explicitly requested). Uses `fwidth(uv)` + `textureSize()` rather
  than `textureQueryLod`, since the latter needs `GL_ARB_texture_query_lod` and this engine
  targets plain GL 3.3 core; `fwidth`/`textureSize` are core since GLSL 130. Implemented as a
  *reduction* on top of the existing view-angle/scale heuristic, not a replacement — the scale
  term stays, since it's what fixed the earlier ridge-duplication bug (large `uParallaxScale`
  needs more layers regardless of on-screen size) and the derivative term addresses a different
  axis (fewer layers when the surface is minified on screen and texture()'s own mip filtering
  has already blurred away the detail extra layers would resolve). See `ParallaxUV` in
  `shaderPBR.frag` (`PARALLAX_LOD_RANGE`, `PARALLAX_LOD_MIN_FRACTION`).
- [ ] Cone-step / relief mapping precompute — only if Phase 1's profiling shows the fixed
  per-pixel layer count is the actual bottleneck on real hardware. Needs an offline bake per
  height map (one-time, not per-frame); real implementation cost, so gate it on a measured
  need, not a hunch.

## Phase 3 — Tessellation / true vertex displacement (trigger-gated, not scheduled)

Do not start this speculatively. Start only when one of these becomes true:

1. **A specific asset needs a correct silhouette** — POM's flat silhouette becomes visible or
   objectionable at the intended camera distance, and no camera-angle workaround exists.
2. **The GL 3.3 → 4.3 upgrade lands for an unrelated reason** (clustered lighting, GPU
   particles) — tessellation becomes a near-free addition once the context bump and `GLShader`
   stage-list rework are already paid for by that other work.
3. **Profiling shows POM's fragment-shader cost scaling worse than tessellation's
   triangle-bound cost would**, measured against the actual hero assets in the scene — not a
   synthetic worst case.

None of these are true right now. Wanting displacement to look "more correct" in the abstract,
or hitting another UV-pole/edge-case artifact, is *not* a trigger — those are POM-tuning
problems (reproduce, isolate, fix the specific term, same as every fix so far this pass), and
tessellation doesn't sidestep them: normal mapping has the identical pole singularity POM does,
for the same underlying reason.
