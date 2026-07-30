# Texture compression / a real texture budget story

The first Phase 2 roadmap item — "Every texture is full-resolution RGBA8 in VRAM the moment its
material loads (see `GLTexture.Decode` — no mip-clamping, no streaming, no BC/KTX compressed
formats)." Every LDR (non-HDR) texture now uploads as BC1 (opaque) or BC3 (alpha) instead of raw
RGBA8, roughly a 4-6x VRAM reduction, with a hand-written encoder rather than a new dependency.

## 1. Why a hand-written encoder, not a library

BC1/BC3 (a.k.a. DXT1/DXT5, part of S3TC) are old, simple, universally-supported formats — every
desktop GPU and driver made in the last two decades decodes them naturally in the texture sampler,
llvmpipe/Mesa included (confirmed below). Encoding one is genuinely small: a fixed 4x4 pixel block,
two endpoint colors, and per-pixel nearest-palette-color indices — no entropy coding, no arithmetic
coding, nothing that benefits from an established library the way, say, a modern video codec would.

Writing `Graphics/Resources/BlockCompression.cs` from scratch avoids adding an external
dependency for something this self-contained — the same reasoning `Docs/Documentation/Gizmos.md`
§1 gives for not pulling in ImGuizmo (avoiding a per-RID native binary the 4-way CI matrix +
headless job would all need). A managed C# BC1/BC3 encoder has no native component at all.

**Quality tradeoff, stated plainly:** endpoint selection is a per-channel bounding box (min/max RGB
across the block), not the principal-component-analysis approach real encoders (`stb_dxt`, etc.)
use. This is a "good enough baseline" — visually solid for typical PBR textures (see §7's
side-by-side) — not best-in-class compressed-texture quality. A PCA-based encoder would be a
drop-in upgrade to `EncodeColorBlock` later if quality ever becomes the bottleneck rather than
VRAM.

## 2. Format selection and the encode itself

`GLTexture`'s LDR upload path (the `TextureData`-taking constructor) now branches three ways:

- **HDR** (`RGB16F`) — unchanged, never compressed. BC6H (the HDR-capable block format) is a
  separate, meaningfully harder encoder; out of scope here since HDR use is limited to skybox
  panoramas today, not the bulk of a project's texture budget.
- **LDR, compression enabled, driver supports it, texture is at least 4x4** — compressed upload
  (`UploadCompressed`, below).
- **Everything else** (compression disabled in config, `GL_EXT_texture_compression_s3tc` missing,
  or a texture smaller than one block) — the original path: raw `TexImage2D` + `GenerateMipmap`.

`BlockCompression.HasAlpha` scans the source image once (any pixel below full opacity) to choose
BC1 (no alpha channel at all, ~6:1 vs. RGBA8) or BC3 (~4:1, but round-trips alpha) — the premultiply
step (`GLTexture.PremultiplyAlpha`, unchanged) already ran before this, so compression sees exactly
the same premultiplied bytes the uncompressed path would have uploaded.

Both formats work in fixed 4x4 blocks: `BlockCompression.EncodeColorBlock` computes a per-channel
bounding box, quantizes the two extremes to RGB565 (nudging them apart if compression would
otherwise land in BC1's alternate 3-color+transparency decode mode — irrelevant for BC3's color
half, which is always 4-color, but needed for BC1 itself to stay fully opaque), expands them back to
888 with the same high-bit-replication rule GPU decoders use, and picks each of the 16 pixels'
nearest palette color by squared RGB distance. `EncodeAlphaBlock` does the equivalent for BC3's
separate 8-value alpha palette, always picking `alpha0 = block max, alpha1 = block min` so it lands
in the higher-precision 8-value interpolation mode rather than the 6-value-plus-hard-0/255 mode
(irrelevant here — nothing needs exact punch-through alpha).

## 3. Mip chain

GPU-side `glGenerateMipmap` generally can't operate on compressed internal formats — they aren't
color/framebuffer-renderable, which mipmap generation relies on. So the compressed path builds and
uploads its own chain: `BlockCompression.Compress` repeatedly box-filter downsamples
(`Downsample`, 2x2 average, edge-clamped) and re-encodes, one level per halving, all the way to
1x1 — the same level count a GPU-generated chain would have
(`floor(log2(max(width,height)))+1`). `UploadCompressed` uploads each level via
`Gl.CompressedTexImage2D(level, ...)` and sets `TEXTURE_MAX_LEVEL` to the chain's actual top level.

Every block-encode call (`Sample`, edge-clamped: reads past the image's right/bottom edge just
clamp to the last valid pixel) works identically whether the block is a clean 4x4 region or a
ragged one — so there's no separate "only compress if divisible by 4" restriction on either the
base texture's own dimensions or any mip level's (which are frequently *not* multiples of 4 once
the chain gets small, e.g. every level below 4x4 itself, or any level of a non-power-of-two source
image). `BlockCompressionTests.Compress_MipChain_EndsAt1x1WithExpectedLevelCount` pins this for a
regular size, a non-power-of-two size, and the 1x1 degenerate case directly.

## 4. Performance: compression must not land on the GL thread

**This bit it on the first real-hardware run.** The version that shipped first did the actual
BC1/BC3 encoding — a nearest-palette search over every pixel of every mip level, real per-texture
CPU work — inside `GLTexture`'s own constructor. That constructor only ever used to do cheap,
GPU-side work (`TexImage2D`/`GenerateMipmap`), so nobody had previously needed the "GL upload" phase
in `ResourceSystem.DecodeAssetsInParallelAndUpload` to happen anywhere but serially on the GL
thread, right after the (already-parallel, `Task.Run`-per-texture) "Parallel decode" phase finished.
Once GLTexture construction started doing real CPU work too, that serial phase — previously a
handful of cheap driver calls — became a straight sum of every texture's compression cost. On a
real 54-texture scene this took the "GL upload" step from ~2.5s to ~27s, a 10x regression, and made
switching a material to a texture nothing had preloaded yet noticeably slower too (same cost, just
on whatever thread the inspector edit ran on).

**The fix: move the actual encoding to wherever decode itself runs, not wherever upload runs.**
`GLTexture.CompressIfEligible(TextureData, bool)` is now a separate, pure-CPU, no-GL-calls static
method — it fills in `TextureData.CompressedLevels` instead of doing anything to a live texture.
`ResourceSystem.DecodeTextureKey` calls it immediately after `Decode`/`DecodeWithOpacity`, in the
*same* call — which for the batch-preload path means it runs inside the same `Task.Run` worker as
decode, across the thread pool, in parallel with every other texture being decoded, exactly the
parallelism decode itself already had. `GLTexture`'s constructor now does zero CPU work: if
`TextureData.CompressedLevels` is already populated, it just uploads those bytes
(`Gl.CompressedTexImage2D` per level) — genuinely cheap, GPU-side, the same category of work
`TexImage2D`/`GenerateMipmap` always were. The on-demand single-texture path (a material switched
to something nothing preloaded) still pays the encoding cost synchronously on whatever thread asks
for it — same as decoding a never-before-seen texture always cost, before compression existed at
all; there's no batch to parallelize a single texture's own compression against.

One wrinkle this move introduced: `CompressIfEligible` needs to know whether
`GL_EXT_texture_compression_s3tc` is supported, but it runs on background threads with no GL
context of their own to ask. `GLTexture.WarmCompressionSupport(GL)` answers that once, eagerly,
from `Engine.InitializeOpenGL` right after the context is created — well before `ResourceSystem`
does any decode work — so the cached answer is already there by the time a background worker needs
it. `CompressIfEligible` treats "not yet warmed" the same as "unsupported" (falls back to
uncompressed) rather than blocking on it, since in the shipped code path it's always warmed first.

**A second, smaller fix found while benchmarking the regression.** `EncodeColorBlock`'s three
4-entry interpolated-palette buffers were built via `Span<int> palR = [r0, r1, ...]` — a collection
expression with non-constant elements, called once per 4x4 block (tens of thousands of times per
texture). Rewriting these as explicit `stackalloc int[4] { ... }` — provably stack-allocated, not
relying on the compiler's collection-expression lowering to avoid a heap array per call — cut a
1024x1024 synthetic-noise benchmark's single-threaded compress time from 157ms to 109ms (~30%). Real
textures (which compress *faster* than random noise, since the nearest-palette search still runs
the same fixed amount of work either way, but real images are far more compressible in principle —
this benchmark measured worst-case work, not best-case) should see the same proportional
improvement. Combined with the threading fix above, a 5-texture live headless comparison in this
sandbox went from an 840ms "GL upload" phase back down to 327ms.

## 5. Texture roles: not everything should compress

**A second real-hardware issue.** On real GPU hardware (this sandbox's llvmpipe render output
doesn't show this — the checkerboard-tile screenshot that surfaced it came from an actual desktop
run), a mirror-like floor material showed visibly blocky, rectangular artifacts inside its specular
highlight, and something that read as two overlapping sun reflections (a sharp bloomed core plus a
separate blocky, unbloomed halo around it). Both symptoms trace to the same cause: the *first*
version of this feature compressed every LDR texture uniformly, including the material's Normal,
Roughness, and Metallic maps.

That's a real mistake, not a subtle one — it's standard, widely-known practice that normal maps in
particular should never go through plain BC1/BC3. A tangent-space normal's X/Y precision maps
almost directly onto lighting-*direction* error, and BC1/BC3's RGB565 endpoints (5/6/5 bits per
channel, one shared pair of colors per 4x4 block) are coarse enough that the error becomes visible
exactly where a viewer's eye is drawn to it: a specular highlight or reflection, where lighting
response amplifies small normal/roughness differences instead of averaging them out the way diffuse
shading would. Real engines default normal maps to BC5/3Dc (two independent 8-bit-precision
channels, no shared endpoints) or leave them uncompressed for exactly this reason — never plain
BC1/BC3. Roughness and Metallic are less universally sensitive but still directly shape a specular
lobe's sharpness, and Height drives parallax offset — all three plausibly contribute to the same
"blocky highlight" family of artifact, so all three were pulled out alongside Normal rather than
trying to isolate which one(s) actually caused this specific screenshot.

**Fix: compression eligibility is now decided per texture *role*, not per texture.** Albedo and AO
stay compression-eligible — both are low-frequency color/occlusion data that diffuse shading
naturally blends across neighboring texels anyway, so BC1/BC3's block quantization doesn't produce
a visible artifact the way it does under specular response. `ResourceSystem.LoadMaterial` routes
Normal/Roughness/Metallic/Height through a new `GetTexture(path, allowCompression: false)` instead
of the plain `Textures.Get(path)` those and Albedo/AO used before; the parallel-preload path
(`PreloadEntities`) builds a parallel `noCompressPaths` set alongside its existing texture-path set,
tagging the same four roles, and threads it through to `DecodeTextureKey`'s `allowCompression`
parameter. `AssetCache<T>` gained a `Contains` check so `GetTexture` can populate the shared cache
with its own (uncompressed) `GLTexture` before falling through to the ordinary `Get` — the ordinary
factory closure, still used directly for Albedo/AO, always allows compression, so a role that needs
it suppressed has to seed the cache first rather than relying on the factory's own decision.

This does shrink the VRAM win — a typical 5-map PBR set (Albedo/Normal/Roughness/Metallic/AO) now
only compresses 2 of 5 textures rather than all 5 — but a correct, smaller VRAM reduction beats a
larger one that visibly damages the render it's supposed to be an invisible optimization for.

## 6. Config, fallback, and visibility

`Render.TextureCompression` (default `true`) is the master switch. Per-texture, compression is
skipped automatically (falling back to the original uncompressed path, not an error) when:

- the texture is HDR,
- `GL_EXT_texture_compression_s3tc` isn't reported by the driver (checked once via
  `GL.IsExtensionPresent`, cached — it can't change mid-session), or
- either dimension is below 4 pixels (the fixed per-block overhead isn't worth it for something
  this small — `ResourceSystem`'s 1x1 default-white fallback texture is the main example).

`StatsOverlay` gained a "Textures" section (`GLTexture.CompressedTextureCount`/
`UncompressedTextureCount`/`TotalApproxBytes`, static counters updated on construct/dispose) so the
budget is actually visible while iterating on a scene, not just something that happened silently at
load time. `ApproxBytes` is an estimate, not a driver query — the exact encoded size for a
compressed texture (summed across its whole mip chain), or `width*height*4` scaled by 4/3 for an
uncompressed one to account for the ~33% a full RGBA8 mip chain adds on top of the base level.
Good enough to eyeball; getting an exact number would mean a `glGetTexLevelParameteriv`
per-level round trip through the driver for every texture, for a number nothing else reads.

## 7. Verification

**Unit tests** (`Centauri.Tests/Graphics/BlockCompressionTests.cs`, 8 tests) — `BlockCompression`
is pure C#, no GL context needed, unlike almost everything else this session's texture/material
work touched. Coverage: `HasAlpha` detection, BC1 vs. BC3 block-size selection, a solid-color
block's decoded RGB landing within a few 8-bit levels of the source (565 quantization is lossy by
design, so exact equality isn't the bar), BC3's alpha endpoints surviving exactly (only the
*interpolated* in-between values are lossy, not the two stored endpoints), and the mip chain's
level count/per-level byte size for a regular, a non-power-of-two, and a 1x1 image. Decoding in the
tests is a second, independent from-spec BC1 implementation (not a call back into
`BlockCompression`'s own private helpers) — a test failure means the *encoded bytes* are wrong, not
just "self-consistent with whatever the encoder already believes." Spot-checked against a
deliberately broken `Pack565` (returning a constant) to confirm the round-trip test actually fails,
not just passes vacuously, matching this repo's established testing convention.

**GPU extension support** — checked directly, not assumed: a temporary headless diagnostic
(`GL.IsExtensionPresent` for `GL_EXT_texture_compression_s3tc`/`GL_ARB_texture_compression_rgtc`/
`GL_ARB_texture_compression_bptc`, reverted before commit) confirmed Mesa's llvmpipe software
rasterizer — what this sandbox's headless CI job actually runs on — reports all three. So the
compressed path isn't merely dormant/untested in CI; the existing `headless-render-smoke-test`
job exercises it for real on every push, same as everything else that boots.

**Live render, both paths** — this sandbox checkout has no `.mat`/`Assets/Objects` content (a
recurring constraint this session — see e.g. `Docs/Documentation/MaterialPersistence.md`), so a
temporary `.mat` (`Testing/CorrugatedIron`'s real albedo/normal/roughness/metallic/AO textures)
and a temporary entity-set JSON (the checked-in `Testing/Trees/Tree.glb` model, referenced by
literal path — both `ModelRegistry`/`MaterialRegistry`'s `ResolvePath` accept a literal path
directly, no registry entry needed) were used for a real bound-material render, then deleted
before commit per the same "throwaway, never committed" convention `CLAUDE.md` documents for
environment/entity-set testing. Two headless captures — `Render.TextureCompression: true` then
`false` — both loaded the same 5 textures + 1 model without error and produced visually
indistinguishable renders of the tree's trunk and leaves at normal viewing distance, the expected
outcome for a lossy-but-solid compressed format.

## 8. What's deliberately out of scope

- **No refcounted texture eviction.** `Render.TextureCacheSize` (and `ModelCacheSize`/
  `ShaderCacheSize`) remain unused — `AssetCache<T>` caches forever, no LRU, no budget-triggered
  free. This was considered and explicitly rejected for this pass: entities/materials hold direct
  references to cached `GLTexture`/`Model` instances with no reference counting anywhere in the
  pipeline, so evicting "the least recently used" without first knowing whether anything still
  points at it risks disposing a GL texture a live `Material` is still bound to — a real
  use-after-free, not a hypothetical one. Building that safely needs a refcounting pass through the
  Entity/Material lifecycle first — its own design effort, the same category of "needs a design
  pass before implementation" the roadmap already called out for LOD/impostors. Compression (a real,
  safe 4-6x reduction with no eviction risk at all) and budget *visibility* (StatsOverlay) are what
  "a real texture budget story" means for this pass; unbounded cache lifetime is the named residual
  gap, not silently unaddressed.
- **No mip-clamping / max-resolution cap.** A texture larger than some configured ceiling still
  decodes and compresses at full source resolution. Compression's ~4-6x reduction is the bigger
  lever by far, so this was left out to keep this pass's scope to one clearly-bounded change — a
  natural, small follow-up (`Render.MaxTextureSize`, downsample via the ImageSharp `Resize` already
  in `GLTexture.Decode`'s dependency list) if VRAM pressure from oversized source art specifically
  becomes the next bottleneck.
- **BC7/ASTC, no runtime format negotiation.** BC7 (higher quality, Mesa reports
  `GL_ARB_texture_compression_bptc` support too — see §7) and ASTC (mobile/tile-based GPUs) aren't
  implemented. BC1/BC3 covers the desktop-GL case this engine targets; a real cross-platform story
  (mobile, particularly) would need per-platform format selection this pass doesn't build.
- **No pre-compressed asset format (KTX2/DDS) support on the *load* side.** Compression happens at
  runtime, every time a texture is decoded — there's no way to ship an already-BC-compressed asset
  and skip the CPU encode step. Not attempted since no such content exists in this project yet and
  it's a separate concern (an asset-pipeline/import-time feature) from the runtime compression this
  pass adds.
