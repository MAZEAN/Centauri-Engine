#version 330 core

in vec2 fUv;

// ─────────────────────────────────────────────────────────────────────────────

// 4x4 Bayer matrix keyed by screen pixel — must stay byte-for-byte identical to
// shaderPBR.frag's copy, so both passes agree exactly on which fragments survive. Hardware
// alpha-to-coverage can't guarantee that agreement across two separate draw calls for the
// same triangle (its per-sample dither is implementation-defined — see
// MainRenderer.ApplySurfaceState's comment for the bug that caused), so this replaces it with
// plain, deterministic math instead. TAA's per-frame sub-pixel jitter shifts which alpha value
// lands on a given fixed screen pixel, so this stochastic pass/fail resolves into a smooth
// gradient over several frames instead of a hard aliased line.
const float BAYER_4X4[16] = float[](
        0.0,  8.0,  2.0, 10.0,
        12.0,  4.0, 14.0,  6.0,
        3.0, 11.0,  1.0,  9.0,
        15.0,  7.0, 13.0,  5.0
);

uniform sampler2D uAlbedo;     // bound only for alpha-tested (foliage) casters
uniform int       uAlphaTest;  // 1 = discard by albedo alpha

// Tunable (RenderConfig.FoliageAlphaCutoff). Must match shaderPBR.frag's own threshold exactly,
// not the shadow caster's/prepass's fixed 0.5 — this pass's whole point is that Forward
// (DepthFunc(Lequal)/no writes) trusts the depth written here as authoritative. If this
// discarded more aggressively than the lit pass does, fragments in the gap between the two
// thresholds would get no real depth written here at all, so overlapping leaf edges in that
// alpha band would have nothing to depth-sort against each other — showing as flickery,
// arbitrarily-ordered noise/fringing right at leaf silhouettes.
uniform float uFoliageAlphaCutoff;

// ─────────────────────────────────────────────────────────────────────────────

float DitherThreshold(vec2 fragCoord)
{
    ivec2 p = ivec2(fragCoord) & 3;
    return (BAYER_4X4[p.y * 4 + p.x] + 0.5) / 16.0;
}

void main()
{
    if (uAlphaTest == 0) return;

    float a = texture(uAlbedo, fUv).a;
    if (a < uFoliageAlphaCutoff || a < DitherThreshold(gl_FragCoord.xy))
        discard;
}
