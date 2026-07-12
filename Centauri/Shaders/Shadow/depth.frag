#version 330 core

in vec2 fUv;

// ─────────────────────────────────────────────────────────────────────────────

uniform sampler2D uAlbedo;     // bound only for alpha-tested (foliage) casters
uniform int       uAlphaTest;  // 1 = discard by albedo alpha so leaves cast a cutout shadow

// Tunable (FoliageConfig.AlphaCutoff) — matches the value the lit pass/prepass use, so the
// shadow silhouette's overall coverage agrees with what's actually rendered, instead of the
// old hardcoded 0.5. Deliberately NOT dithered like prepass.frag/shaderPBR.frag's copies of
// this cutout: those are blended (GTAO/lit color), where dithering trades a hard edge for
// per-pixel noise that resolves into a smooth gradient. A shadow *caster* has no blending to
// resolve into — the dither pattern is keyed to shadow-map texel coordinates (gl_FragCoord in
// the depth pass), so every cascade redraw re-rasterizes it against a slightly different
// light-space projection, and the dapple pattern visibly swims independently of (and on top
// of) whatever the underlying cascade fit is doing. See CascadeBuilder's texel-snap fix for
// the cascade-fit side of this — dithering here amplified that same instability into
// per-pixel noise instead of a coherent, easier-to-read edge shift.
uniform float uFoliageAlphaCutoff;

// ─────────────────────────────────────────────────────────────────────────────

void main()
{
    // match the lit/prepass cutout's coverage so foliage casts leaf-shaped (dappled) shadows
    // instead of solid quad blocks
    if (uAlphaTest == 1 && texture(uAlbedo, fUv).a < uFoliageAlphaCutoff)
        discard;
}
