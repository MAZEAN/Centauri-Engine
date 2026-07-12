#version 330 core

in vec2 fUv;

// ─────────────────────────────────────────────────────────────────────────────

// 4x4 Bayer matrix keyed by screen pixel — byte-for-byte identical to shaderPBR.frag's,
// prepass.frag's and zprepass.frag's copies. Must match exactly: a looser cutout here casts a
// shadow silhouette that doesn't agree with the lit pass's own leaf shape, showing as a visible
// mismatch (shadow edge doesn't line up with the rendered leaf edge) at foliage silhouettes.
const float BAYER_4X4[16] = float[](
        0.0,  8.0,  2.0, 10.0,
        12.0,  4.0, 14.0,  6.0,
        3.0, 11.0,  1.0,  9.0,
        15.0,  7.0, 13.0,  5.0
);

uniform sampler2D uAlbedo;     // bound only for alpha-tested (foliage) casters
uniform int       uAlphaTest;  // 1 = discard by albedo alpha so leaves cast a cutout shadow

// Tunable (FoliageConfig.AlphaCutoff). Must match shaderPBR.frag's/prepass.frag's/
// zprepass.frag's threshold exactly — see the BAYER_4X4 comment above.
uniform float uFoliageAlphaCutoff;

// ─────────────────────────────────────────────────────────────────────────────

float DitherThreshold(vec2 fragCoord)
{
    ivec2 p = ivec2(fragCoord) & 3;
    return (BAYER_4X4[p.y * 4 + p.x] + 0.5) / 16.0;
}

void main()
{
    // match the lit/prepass cutout so foliage casts leaf-shaped (dappled) shadows that line up
    // with the actual rendered leaf edge, instead of a looser/solid quad block
    if (uAlphaTest == 1)
    {
        float a = texture(uAlbedo, fUv).a;
        if (a < uFoliageAlphaCutoff || a < DitherThreshold(gl_FragCoord.xy))
            discard;
    }
}