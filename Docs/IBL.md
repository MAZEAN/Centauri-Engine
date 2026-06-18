# Image-Based Lighting (IBL)

> How the IBL system works, how to use it to build scenes, and how to tune it.

IBL makes objects receive **ambient light and reflections from the surrounding HDR
sky**, instead of only from analytic lights. Rough/dielectric surfaces pick up soft
sky colour; smooth/metallic surfaces show sharp environment reflections. It replaces
the old flat `vec3(0.03)` ambient term.

---

## How it works

Two phases:

- **Bake** (once, at scene load) — turn each HDR panorama into three precomputed
  lookups: a **diffuse irradiance** cubemap, a **specular prefiltered** cubemap (with
  mips for roughness), and a shared **BRDF LUT**.
- **Sample** (every frame, in the PBR shader) — each surface looks up ambient diffuse
  by its normal, glossy reflection by its reflection vector, and combines them via the
  BRDF LUT.

```
HDR panorama (equirect 2D, RGB16F)
   │  bake  (IBLBaker, at load)
   ├─► env cubemap ─► irradiance cubemap    (diffuse ambient)
   │                └► prefiltered cubemap   (glossy reflections, per-roughness mips)
   └─► BRDF LUT (environment-independent, baked once)
                          │  sample  (shaderPBR.frag, per frame)
                          └─► ambient = diffuse + specular  →  added to direct lighting
```

The bake runs at startup for **every** skybox, so switching environments at runtime
just swaps pre-baked maps with no hitch.

---

## Files

### Loading the HDR environment

- **`Graphics/Resources/HighDynamicRange/HDRLoader.cs`** — dispatches by extension:
  `.hdr` → `RadianceHDRDecoder`, `.exr` → TinyEXR. Returns linear float RGB and
  **clamps all pixels to 65504** (half-float max) so the texture can never hold `+Inf`
  (an `Inf` here propagates into the IBL bake as `NaN`).
- **`…/RadianceHDRDecoder.cs`** — in-house Radiance RGBE decoder (handles the RLE
  variants); also defines the `HDRImage` struct (pixels + dimensions).
- **`Graphics/Resources/GLTexture.cs`** (`LoadHDR`) — uploads the float data as an
  **RGB16F** 2D texture: the raw equirectangular panorama everything is derived from.

### The bake — `Rendering/IBL/IBLBaker.cs` + shaders in `Assets/Shaders/IBL/`

`IBLBaker` owns the capture FBO, a cube mesh, the 6 cube-face view matrices, the four
programs, and the BRDF LUT (baked once in its constructor). `Bake(equirect, exposure)`
runs three render-to-cubemap passes (backface culling disabled — you capture from
inside the cube):

1. **`cube.vert` + `equirect_to_cubemap.frag`** — projects the panorama onto 6 cube
   faces using the same `atan/asin` mapping as the skybox, multiplied by the per-sky
   **exposure** and clamped finite → the **env cubemap** (mipmapped; used by the
   prefilter pass).
2. **`irradiance.frag`** — integrates the cosine-weighted hemisphere of the env per
   output direction → **diffuse irradiance** (what a matte surface receives from the
   whole sky). Uses a smooth highlight rolloff to tame bright sky without a hard edge.
   Output: a 64² irradiance cubemap.
3. **`prefilter.frag`** — GGX importance-samples the env per roughness, once per mip
   (mip 0 = mirror-sharp, higher mips = blurrier) → **glossy reflections at every
   roughness**. Output: a 128² prefiltered cubemap with 5 mips.
4. **`brdf.frag`** (+ reuses `Post/post.vert`) — integrates the split-sum BRDF into a
   512² **RG16F LUT** indexed by `(NdotV, roughness)`. Environment-independent, baked
   once.

### Storage & wiring

- **`World/Collections/SkyboxSet.cs`** — each `Skybox` holds its panorama `GLTexture`,
  per-sky `Exposure`/`BlackLevel`, and the baked `IrradianceMap`/`PrefilteredMap`
  handles. `IblBaked` is true once filled.
- **`Rendering/RenderingSystem.cs`** — owns the `IBLBaker`; `BakeEnvironments(scene)`
  (called from `Engine.LoadScene`) bakes every skybox up front.
- **`Rendering/Renderers/MainRenderer.cs`** — `BindIbl(scene)` binds the **active**
  skybox's irradiance (unit 5) and prefilter (unit 6) cubemaps and the shared BRDF LUT
  (unit 7) once per frame; `UploadGlobalUniforms` sets the sampler units, `uHasIBL`,
  `uMaxReflectionLod` (= mips − 1 = 4) and `uIblIntensity`.
- **`Loading/SceneDefinition.cs` + `SceneLoader.cs`** — parse the `skybox` entries.
- **`Config/AppConfig.cs`** — holds `Render.IblIntensity`.

### Using it — `Assets/Shaders/PBR/shaderPBR.frag`

```glsl
kS       = FresnelSchlickRoughness(NdotV, F0, roughness);            // grazing reflectivity
kD       = (1 - kS) * (1 - metallic);                               // metals have no diffuse
diffuse  = irradiance(N) * albedo;                                  // ambient diffuse
specular = prefilter(R, roughness * uMaxReflectionLod) * (kS*brdf.x + brdf.y);
ambient  = (kD*diffuse + specular) * ao * uIblIntensity;
color    = ambient + Lo;                                            // + direct lights
```

The shader outputs linear HDR; the post pass tonemaps once.

---

## Building scenes with IBL

The system is data-driven — no code needed to author a scene:

1. **Drop an HDR panorama** (`.hdr` or `.exr`, equirectangular) into
   `Assets/Textures/Skybox/`.
2. **Add a skybox entry** in the scene JSON:
   ```json
   { "name": "Day", "panorama": "Assets/Textures/Skybox/DaySky.hdr",
     "exposure": 0.5, "blackLevel": 0.02, "active": true }
   ```
   It bakes automatically at load and lights every PBR object.
3. **Author materials normally** — proper roughness/metallic maps. IBL "just works":
   chrome reflects, brick stays matte, everything picks up sky colour in shadow.
4. **Multiple environments** — list several skyboxes; all bake at load. Switch with
   `Cycle()` (key `B`) or `SetActive(name)` — instant, no re-bake.
5. **Day/night cycle (future)** — blend two pre-baked environments' irradiance/prefilter
   by a time-of-day factor (a small shader addition).

Practical rules:

- IBL only knows the **sky**, not scene geometry — mirror objects reflect the sky, not
  the floor. (Scene reflections need SSR or reflection probes — a future feature.)
- Use an **icosphere** (not a UV sphere) for reflective spheres to avoid the pole pinch.
- Objects always also receive the **direct lights** (sun/point/spot); IBL is the
  ambient/indirect layer on top.

---

## Parameter reference

| Parameter | Where | Default | Min → Max | What it does / how to tune |
|---|---|---|---|---|
| **`exposure`** (per-sky) | scene.json | 1.0 | ~0.05 → ~16 | Scales the panorama's brightness for **both** the displayed sky **and** the baked IBL. Lower for over-bright midday HDRIs, raise for dim ones. Changing it re-bakes. |
| **`blackLevel`** (per-sky) | scene.json | 0.0 | 0.0 → ~0.5 | Crushes the **sky backdrop's** dark floor to black (not the IBL). Tiny values (0.01–0.05) deepen night skies. |
| **`iblIntensity`** (global) | config.json `render` | 0.3 | 0.0 → ~2.0 | Master strength of IBL on objects, **decoupled** from sky brightness. `0` = no IBL ambient; `1` ≈ full physical. **The main "objects too bright/dark" knob.** |
| **`MaxRadiance`** | `IBLBaker.cs` const | 10 | ~2 → ~50 | HDR knee used when baking irradiance/prefilter. Lower = more taming (flatter IBL); higher = punchier highlights but risk of fireflies. Recompile to change. |
| **`EnvSize`** | `IBLBaker.cs` | 512 | 256 → 1024 | Capture cubemap resolution. Higher = sharper reflections, slower bake. |
| **`IrradianceSize`** | `IBLBaker.cs` | 64 | 16 → 128 | Diffuse map resolution. 32–64 is plenty (low-frequency). |
| **`PrefilterSize` / `PrefilterMips`** | `IBLBaker.cs` | 128 / 5 | 128–256 / 5–6 | Glossy reflection resolution & roughness levels. 256 for crisper mirrors. |
| **Material `roughness`** | material | per-asset | 0.0 → 1.0 | Picks the prefilter mip: 0 = mirror, 1 = diffuse-like. Drives diffuse↔specular balance. |
| **Material `metallic`** | material | per-asset | 0.0 → 1.0 | 1 = no diffuse, full colored reflection; 0 = dielectric (diffuse + faint Fresnel sheen). |
| **Grading `exposure`** | config.json `grading` | 1.0 | ~0.2 → ~4 | Final-image exposure (whole frame, after everything). Use for overall mood / headroom. |

### How the exposure knobs relate

1. **per-sky `exposure`** — scales sky + its baked IBL together ("how bright is this
   environment").
2. **`iblIntensity`** — scales IBL's effect on *objects only* (decouples lighting
   strength from how bright the sky looks).
3. **`MaxRadiance`** — caps the dynamic range fed into the IBL bake (anti-blowout /
   anti-firefly).
4. **grading `exposure`** — final image exposure on the whole frame.

**Recommended tuning order:** set per-sky `exposure` so the **sky** looks right → dial
`iblIntensity` until **objects** look right → leave `MaxRadiance` at 10 unless you see
fireflies (raise) or want a flatter look (lower) → use grading `exposure` only for the
overall mood.

---

## Gotchas & notes

- **`+Inf` → `NaN`:** an HDR sun brighter than 65504 overflows RGB16F to `+Inf`, which
  the IBL convolution turns into `NaN` (renders black on some GPUs, white on others).
  Guarded by the loader clamp **and** the equirect→cubemap clamp — keep both.
- **Resource ownership:** the baked cubemaps are GL handles stored on each `Skybox`.
  Ensure they're deleted on teardown (e.g. tracked and freed by `IBLBaker.Dispose`) to
  avoid leaks on scene reload.
- **AO on specular:** `ambient = (kD*diffuse + specular) * ao` applies AO to the
  specular term too — the standard LearnOpenGL shortcut; fine in practice.
- **Sun double-counting:** the HDRI's sun and the analytic directional light both light
  the scene; the `MaxRadiance` clamp keeps the HDRI sun from doubling the diffuse. Treat
  the analytic directional as the "real" sun (and the shadow caster, once shadows exist).
