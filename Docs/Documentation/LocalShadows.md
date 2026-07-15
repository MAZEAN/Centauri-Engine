# Local-Light Shadows (Spot Lights)

Shadow maps for spot lights, opted in per-light. Independent of the directional-sun CSM/PCSS pass
(`ShadowConfig`/`ShadowMapper`) — the two are separate GL resources, separate config blocks, and
neither's `enabled` flag affects the other.

**Point lights are not covered.** See "Why spot only" below.

## 1. Turn it on

Two switches, both required:

1. `spotShadows.enabled` in `config.json` (default `true`) — the pass's master switch; `false`
   skips it entirely regardless of any individual light.
2. Per-light: select the spot light entity → Inspector → Light → **Casts Shadow**. Off by
   default even with (1) on — every shadow-casting light is a real extra render pass, so nothing
   pays for it until asked.

```jsonc
"spotShadows": {
  "enabled": true,
  "size": 1024,        // per-slot atlas resolution (256/512/1024/2048 in the inspector)
  "depthBias": 0.0015,
  "normalBias": 2.5,
  "pcfRadius": 2
}
```

Fields map 1:1 onto `Config/Settings/SpotShadowConfig.cs`; tunable live from the Inspector's
**Spot Shadows** section (`SpotShadowSection.cs`), next to (but independent of) **Shadows** (CSM).

A shadow-casting spot light also gets a **Shadow Range** field (world units, default 25) —
the shadow frustum's far plane. Distinct from the light's actual falloff (still governed by the
constant/linear/quadratic attenuation `MainRenderer` applies): this only bounds how far the depth
pass's perspective projection reaches, so it wants to roughly match where the light stops
mattering visually rather than being tuned precisely.

## 2. How it works

`SpotShadowMapper` (`Rendering/Shadows/SpotShadowMapper.cs`) — a separate, simpler sibling to
`ShadowMapper`, not a subclass or a generalization of it:

- **One shared atlas**, not one texture per light: a single `ShadowArray` (the same
  `Texture2DArray` + FBO wrapper CSM's cascades use) with `SpotShadowConfig.MaxShadowSpots`
  layers (hard cap, `4` — a real GPU resource, mirrors `ShadowConfig.MaxCascades`). Every
  shadow-casting spot light gets one layer.
- **Nearest-to-camera selection**: if more lights have `CastsShadow` set than there are atlas
  slots, the `MaxShadowSpots` nearest to the camera win each frame — graceful degradation, not an
  error, in the same spirit as `LightingSystem`'s own `MAX_POINT_LIGHTS`/`MAX_SPOT_LIGHTS` caps.
- **Stable slot assignment**: a light keeps the same atlas layer for as long as it stays selected
  (`SpotShadowMapper.AssignSlots`), so an unrelated light losing/gaining a slot elsewhere in the
  scene never forces a redraw of this one.
- **Per-slot redraw cache**: much simpler than `ShadowCache` — no texel-snap/stable-fit machinery
  (a perspective local-light frustum has no analogous "stable fit" to preserve across small camera
  moves the way CSM's fixed-origin ortho fit does). A slot only redraws when its light's own
  position/direction/cutoffs/range actually changed, or `Scene.Revision` moved (a conservative
  proxy for "a caster in range might have moved") — see `SlotSnapshot`.
- **No PCSS contact-hardening** — deliberately, unlike CSM. A fixed-radius PCF kernel
  (`SpotShadowFactor` in `shaderPBR.frag`) keeps the per-light shader cost and the config surface
  small; a local light close to its casters rarely needs the same soft, distance-varying penumbra
  a huge sun/sky light does. `ShadowArray`'s "raw" uncompared copy (which PCSS's blocker search
  would need) is therefore allocated but never synced/bound in this pass.
- **Casting**: reuses `Shaders/Shadow/depth.vert`/`depth.frag` completely unchanged — that shader
  was already projection-agnostic (nothing in it assumes orthographic).

Which atlas layer (if any) a shadow-casting spot light landed in is carried on the light's own
entry in the `Lights` UBO (`SpotLight.cutoffs.z`, otherwise-unused padding — see
`LightBuffer.AddSpot`), not a separate lookup table: `shaderPBR.frag`'s `CalcSpotLight` decodes it
directly (`0` = no shadow, `N+1` = atlas layer `N`) and samples `SpotShadowFactor` before applying
`* (1.0 - shadow)`, mirroring the directional light's own `* (1.0 - shadow)` pattern in
`DirectLighting`.

## 3. Why spot only (point lights deferred)

GL 3.3 core has no cubemap array (`GL_TEXTURE_CUBE_MAP_ARRAY` / `ARB_texture_cube_map_array` is
GL 4.0+) — there's no way to batch several point-light cubemaps into one texture the way this
batches spot frustums into one `Texture2DArray`. A point-light shadow would need either:

- A `GL_TEXTURE_CUBE_MAP` **per** shadow-casting point light (6 perspective 90° passes each,
  individually bound — expensive per-light and squeezes the texture-unit budget further; only
  units 14-15 were free before this feature, and this feature just spent unit 14), or
- Approximating with a single spot-like frustum per point light (loses omnidirectionality).

Neither is a natural extension of what's here — it's new plumbing either way. Spot lights, by
contrast, are naturally a single perspective frustum, directly analogous to one CSM cascade, and
could reuse `ShadowArray` almost unchanged. That asymmetry is why this pass exists for spot lights
now and point lights don't, not an oversight.

**Trigger for revisiting**, mirroring `Docs/Roadmaps/DISPLACEMENT.md`'s staged-gating style — do
not start speculatively:

1. A specific scene actually needs a shadow-casting point light (an omnidirectional source with
   no natural single-frustum substitute), or
2. The GL 3.3 → 4.3 upgrade lands for an unrelated reason (clustered lighting, GPU particles) —
   `samplerCubeArray` becomes available essentially for free once that context bump is paid for.

## 4. Verify it

No in-engine test project; verified with a headless run
(`CENTAURI_HEADLESS_FRAMES`/`CENTAURI_SCREENSHOT_PATH`, see `CLAUDE.md`) since this is a GPU-side
feature end to end — no standalone-harness path like `PhysicsEngine.md`'s.

- A shadow-casting spot light lighting dense foliage (`Testing/Trees/Tree.glb`, since this sandbox
  ships no `Assets/Objects` content) produces visibly darker, spatially coherent patches within
  the canopy — not a uniform wash — confirming `SpotShadowFactor` is modulating per-pixel, not a
  no-op.
- Differential check: the *same* scene/camera/light with `castsShadow: false` shows uniformly lit
  foliage (only normal-based shading variation) — confirms the darkening above is actually driven
  by the new shadow path, not some other AO/lighting term.
- The engine boots and shuts down cleanly headless with the pass active — in particular, this
  exercises the actual GLSL compiling and linking successfully (`GLShader` throws on a compile/
  link failure, so a clean headless run is a real compile check, not just a C#-side smoke test).

## 5. Known limitations / next steps

- **No point-light shadows** — see §3.
- **No kinematic-light support** — a shadow-casting spot light attached to a moving entity works
  (the per-slot cache redraws whenever its position changes), but there's no throttling for a
  *continuously* moving light the way `ShadowConfig.LightThrottleMs` throttles a slowly-rotating
  sun; a spot light animated every frame redraws its slot every frame. Fine at the current
  `MaxShadowSpots` = 4 scale; revisit if that becomes a real cost.
- **Fixed PCF radius, no contact hardening** — see §2. Straightforward to add later (the
  `ShadowArray` raw-texture infrastructure it'd need is already allocated, just unused) if a scene
  needs it, following the exact `FindBlockerDepth`/penumbra-clamp pattern CSM already uses.
- **`MaxShadowSpots` = 4 is a compile-time-adjacent constant** (`SpotShadowConfig.MaxShadowSpots`,
  mirrored in `shaderPBR.frag`'s `MAX_SHADOW_SPOTS`) — raising it means editing both and is not
  currently exposed as a runtime config value, matching `ShadowConfig.MaxCascades`'s own precedent.
