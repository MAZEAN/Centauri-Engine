# GL 3.3 → 4.x Upgrade

Tracks the OpenGL context-version upgrade named in `Docs/TODO.md` ("GL 3.3 → 4.3 upgrade — enables
clustered lighting, GPU particles & cheaper foliage"). Mirrors `Docs/Roadmaps/DISPLACEMENT.md`'s
staged, trigger-gated structure: land the free part, then adopt each 4.3-only feature only when a
concrete blocked item actually needs it — not speculatively.

## Status snapshot

**Step 1 — context bump — done.** `WindowManager.CreateWindowOptions` now requests
`GraphicsAPI(OpenGL, Core, ForwardCompatible, 4.3)` instead of Silk.NET's default 3.3 core request.
Every shader still declares `#version 330 core` and keeps compiling/running unmodified — a 4.3 (or
higher) core context is backward compatible with 3.3-era GLSL, so this alone changes nothing
observable. Verified headless: Mesa/llvmpipe under this sandbox negotiates **4.5 core** (a version
request is a floor, not an exact match — the driver hands back the highest compatible context),
and the existing shadow/spot-shadow/physics/foliage rendering paths all still produce correct
output under it (see the session's headless screenshots — same visual result as under 3.3).

`Engine.InitializeOpenGL` now logs the actually-negotiated `GL_VERSION`/`GL_RENDERER` at startup
(`[Engine] GL: ...`), so a driver silently granting something unexpected (lower than requested, or
a compatibility rather than core profile) surfaces immediately instead of showing up later as a
confusing GLSL/feature-availability bug.

**Step 2 — adopting 4.3-only features — not started.** See "Trigger table" below; nothing here is
scheduled, each row activates independently when its blocked item is picked up.

## Why 4.3 specifically

| Version | What it adds (relevant subset) |
|---|---|
| 4.0 | Texture cube map arrays (`samplerCubeArray`), tessellation shaders |
| 4.1 | Viewport arrays, separate shader objects |
| 4.2 | Immutable texture storage (`ARB_texture_storage`), atomic counters, image load/store |
| 4.3 | **Compute shaders**, **SSBOs**, multi-draw-indirect, `KHR_debug`, texture views |

4.3 is the practical target because it bundles the three features with the most leverage here
(compute, SSBOs, indirect draw) rather than stopping at 4.0/4.1's narrower wins.

## Trigger table — adopt each only when its row's condition is true

Do not start any of these speculatively — same discipline `DISPLACEMENT.md` applies to
tessellation. Each is independently shippable; none depends on another being done first except
where noted.

| Feature | Unlocks | Trigger |
|---|---|---|
| Texture cube map arrays | Point-light shadows (`PointShadowMapper`, mirroring `SpotShadowMapper` — see `Docs/Documentation/LocalShadows.md` §3) | A scene actually needs an omnidirectional shadow-casting light with no workable single-frustum substitute. |
| Tessellation shaders | True vertex displacement for POM (`Docs/Roadmaps/DISPLACEMENT.md` Phase 3) | Any of that doc's own Phase 3 triggers — a specific asset needing a correct silhouette, or profiling showing POM's per-pixel cost losing to tessellation's triangle-bound cost. |
| SSBOs | Removes `MAX_POINT_LIGHTS`/`MAX_SPOT_LIGHTS = 16` and `SpotShadowConfig.MaxShadowSpots = 4` as hard caps (see `Docs/Documentation/TextureUnits.md`) | A real scene actually hits one of those caps — not before. |
| Compute shaders | GTAO/SSR/Bloom/TAA/auto-exposure ported from fragment passes; prerequisite for clustered light culling and GPU-driven culling | Profiling shows one of the fragment-pass versions is the actual bottleneck, *or* clustered lighting becomes needed for a light-count reason (which itself wants the SSBO row done first). Requires the `GLShader` stage-list rework (add `.comp` as a recognized stage) — pay that cost once, on whichever of these lands first. |
| Multi-draw-indirect | GPU-driven culling/instancing for `MainRenderer`/`ShadowMapper`/`SpotShadowMapper`'s per-`Model` CPU-side bucketing — the "cheaper foliage" TODO item | CPU-side cull+bucket (`CullingSystem` + the `Dictionary<Model, List<InstanceData>>` pattern) profiles as the bottleneck in a scene with many dynamic/foliage instances. |
| Immutable texture storage | Mechanical `glTexImage*` → `glTexStorage*` swap across `GLTexture`/`ShadowArray`/`HDRFramebuffer`/render targets | Low-risk, low-value alone — bundle into whichever other row is being worked, don't do standalone. |
| `KHR_debug` | Synchronous GL error callback instead of manual `glGetError()` polling | Whenever GL-call debugging friction actually shows up during other work — cheap to add opportunistically. |

## Known constraint: macOS

Apple caps native OpenGL at 4.1 and has not moved since (no 4.2+, ever). If this engine ever needs
to run on macOS, GL 4.3 core is unavailable there outright — not a driver-version problem, a
platform ceiling. That would mean either stopping at 4.1 (losing compute/SSBO/indirect-draw) with a
platform branch, or a translation layer (MoltenVK/ANGLE), which is a materially bigger project than
this upgrade itself. Settle whether macOS matters *before* adopting any 4.3-only feature from the
trigger table — Step 1 (the context bump already landed) doesn't foreclose either answer, since a
future macOS build would simply request a lower `APIVersion` in `WindowManager`.

Headless CI is unaffected either way: Mesa's llvmpipe software rasterizer already supports GL 4.5
core (including compute shaders), confirmed by this session's own headless runs.
