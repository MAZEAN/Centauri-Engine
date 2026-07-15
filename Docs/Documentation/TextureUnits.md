# GL Texture Unit Map

Which `GL_TEXTUREn` slot each resource binds to, engine-wide. Written while investigating the
GL 3.3 → 4.x upgrade (`Docs/Roadmaps/GL4_UPGRADE.md`) — the lit forward pass is the one place
texture units are actually a scarce, shared resource; everything else below it is a self-contained
single-purpose pass that rebinds its own low-numbered units immediately before its own draw call
and never runs concurrently with another pass's bindings, so those don't compete for the same
budget the way the lit pass's fixed set does.

## The scarce one: the lit forward pass (`shaderPBR.frag` / `zprepass.frag` / `prepass.frag`)

Bound once per frame in `MainRenderer.BeginFrame` (`BindGtao`/`BindIbl`/`BindShadows`/
`BindSpotShadows`) plus per-material in `TextureBinder.BindMaterial`, and held bound for every
draw call in the main pass. **14 of 16 units are occupied; only unit 15 is free.** GL 3.3 core only
*guarantees* `GL_MAX_TEXTURE_IMAGE_UNITS ≥ 16` per fragment stage — real hardware reports far more,
but the engine has always coded defensively to the guaranteed minimum (see `MainRenderer.cs`'s own
comments), so this table is the actual constraint, not a paper one.

| Unit | Uniform | Resource | Bound by |
|---|---|---|---|
| 0 | `uAlbedoMap` | Material albedo | `TextureBinder.BindMaterial` |
| 1 | `uNormalMap` | Material normal map | `TextureBinder.BindMaterial` |
| 2 | `uRoughnessMap` | Material roughness map | `TextureBinder.BindMaterial` |
| 3 | `uMetallicMap` | Material metallic map | `TextureBinder.BindMaterial` |
| 4 | `uAOMap` | Material AO map | `TextureBinder.BindMaterial` |
| 5 | `uIrradianceMap` | IBL diffuse irradiance cubemap | `MainRenderer.BindIbl` |
| 6 | `uPrefilterMap` | IBL specular prefiltered cubemap | `MainRenderer.BindIbl` |
| 7 | `uBrdfLUT` | Split-sum BRDF lookup texture | `MainRenderer.BindIbl` |
| 8 | `uShadowMapNear` | CSM near-tier depth (compare mode) | `MainRenderer.BindShadows` |
| 9 | `uGtaoMap` | GTAO ambient occlusion | `MainRenderer.BindGtao` |
| 10 | `uShadowMapNearRaw` | CSM near-tier depth (raw, PCSS blocker search) | `MainRenderer.BindShadows` |
| 11 | `uShadowMapFar` | CSM far-tier depth (compare mode) | `MainRenderer.BindShadows` |
| 12 | `uShadowMapFarRaw` | CSM far-tier depth (raw, PCSS blocker search) | `MainRenderer.BindShadows` |
| 13 | `uHeightMap` | Material height map (POM) | `TextureBinder.BindMaterial` |
| 14 | `uSpotShadowMap` | Spot-light shadow atlas (compare mode) | `MainRenderer.BindSpotShadows` |
| **15** | — | **free** | — |

Notes:
- Units 8/10 and 11/12 are deliberate pairs: the *same* depth data in two texture objects, one
  with `GL_COMPARE_REF_TO_TEXTURE` (hardware PCF, sampled as `sampler2DArrayShadow`) and one
  without (sampled as a plain `sampler2DArray` for PCSS's blocker search) — see `ShadowArray`'s own
  comment for why one texture can't serve both roles.
- The spot-shadow atlas (unit 14) deliberately has **no** raw/uncompared counterpart — that pass
  has no PCSS contact-hardening (see `Docs/Documentation/LocalShadows.md` §2), so nothing ever
  samples it uncompared. `ShadowArray` still allocates one internally (shared code with CSM), it's
  just never bound here.
- `zprepass.frag` and `prepass.frag` only ever use unit 0 (`uAlbedo`, alpha-tested casters only) —
  they don't share the lit pass's full occupancy, since they run in an earlier, separate pass with
  their own bind state.

**This table is what actually motivates the SSBO-based light/shadow-data row of
`GL4_UPGRADE.md`'s trigger table** — every one of units 0–14 is committed to something that's either a fixed-size
per-material slot or a fixed-size shared resource (one CSM, one spot atlas); there's no room left
to add a second local-light shadow atlas, a third IBL source, or anything else past unit 15 without
either dropping something or restructuring how these are bound (texture arrays already did this
once for CSM/spot shadows — the same move a `samplerCubeArray` would make for point lights, see
`LocalShadows.md` §3).

## Everything else: self-contained single-pass shaders

Each of these binds its own inputs to low unit numbers (0, 1, 2, …) immediately before its own
draw call, then the next pass does the same — no cross-pass reservation needed, since passes never
execute concurrently. Listed for reference, grouped by pass; unit numbers are *local* to each pass,
not a continuation of the table above.

| Pass | Shader(s) | Units 0 → n |
|---|---|---|
| Prepass (depth/normal/material G-buffer) | `prepass.frag` | 0 = `uAlbedo` (alpha-tested only) |
| Z-prepass | `zprepass.frag` | 0 = `uAlbedo` (alpha-tested only) |
| GTAO — main pass | `gtao.frag` | 0 = `uDepth`, 1 = `uNormal`, 2 = `uNoise` |
| GTAO — blur | `gtao_blur.frag` | 0 = `uGtao`, 1 = `uDepth` |
| GTAO — temporal | `gtao_temporal.frag` | 0 = `uCurrent`, 1 = `uHistory`, 2 = `uDepth` |
| SSR — trace | `ssr.frag` | 0 = `uScene`, 1 = `uDepth`, 2 = `uNormal`, 3 = `uMaterial` |
| SSR — blur | `ssr_blur.frag` | 0 = `uSsr`, 1 = `uMaterial` |
| SSR — resolve | `ssr_resolve.frag` | 0 = `uSsr`, 1 = `uDepth`, 2 = `uNormal`, 3 = `uMaterial`, 4 = `uBrdfLUT`, 5 = `uPrefilterMap` (cube), 6 = `uProbeMap` (cube), 7 = `uGtaoMap`, 8 = `uPlanarMap` |
| SSR — temporal | `ssr_temporal.frag` | 0 = `uCurrent`, 1 = `uHistory`, 2 = `uHistoryZ`, 3 = `uDepth` |
| TAA — velocity | `velocity.frag` | 0 = `uDepth` |
| TAA — resolve | `taa.frag` | 0 = `uCurrent`, 1 = `uHistory`, 2 = `uVelocity`, 3 = `uSsr` |
| Bloom — prefilter/down/up | `bloom_prefilter.frag`, `bloom_down.frag`, `bloom_up.frag` | 0 = `uSrc` |
| Auto-exposure — prefilter/adapt | `luminance_prefilter.frag`, `luminance_adapt.frag` | 0 = `uSrc`/`uCurrent` (+ 1 = `uPrevious` for adapt) |
| Post / tonemap | `post.frag` | 0 = `uHdr`, 1 = `uBloom`, 2 = `uSsr`, 3 = `uAutoLuminance` |
| Skybox | `skybox.frag` | 0 = `uPanorama`, 1 = `uCloudMap` |
| IBL bake — equirect→cubemap | `equirect_to_cubemap.frag` | 0 = `uEquirect` |
| IBL bake — irradiance/prefilter | `irradiance.frag`, `prefilter.frag` | 0 = `uEnv` (cube) |
| Debug G-buffer view | `buffer.frag` | 0 = `uNormal`, 1 = `uDepth`, 2 = `uAo`, 3 = `uVelocity` |

## Why this matters for the GL 4.x upgrade

Bindless textures (the "just don't run out of units" fix) aren't GL 4.3 core — that's still a
vendor extension (`ARB_bindless_texture`), widely supported but not guaranteed the way core
features are. What GL 4.3 core *does* give this specific problem:

- **SSBOs** let `LightBuffer`/`ShadowBuffer`/`SpotShadowBuffer` carry per-light/per-shadow data as
  a dynamically-sized buffer instead of a fixed std140 array — doesn't free a texture unit by
  itself, but removes the pressure to add *more* fixed-size texture-array resources (each of which
  currently costs 1-2 units) as a way to scale up shadow/light counts.
- **Texture cube map arrays** (GL 4.0) let a future point-light shadow pass share one
  `samplerCubeArray` across every shadow-casting point light — one new unit for potentially several
  lights, the same trade CSM and spot shadows already made versus one texture per cascade/light.
