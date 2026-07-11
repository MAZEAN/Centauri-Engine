#version 330 core

in vec3 vViewNormal;
in vec2 fUv;

// ─────────────────────────────────────────────────────────────────────────────

// 4x4 Bayer matrix keyed by screen pixel — byte-for-byte identical to shaderPBR.frag's and
// zprepass.frag's copies. Must match exactly: this pass's depth is trustworthy for Forward's
// early-Z reuse (see RenderingSystem) only when it agrees pixel-for-pixel with what the lit
// pass would keep or discard — a looser cutout here would leave fragments in the gap between
// the two thresholds with no real depth written, showing as flickery fringing at leaf silhouettes.
const float BAYER_4X4[16] = float[](
        0.0,  8.0,  2.0, 10.0,
        12.0,  4.0, 14.0,  6.0,
        3.0, 11.0,  1.0,  9.0,
        15.0,  7.0, 13.0,  5.0
);

layout (location = 0) out vec4 gNormal;   // view-space normal, encoded to [0,1]
layout (location = 1) out vec4 gMaterial;

uniform sampler2D uAlbedo;     // bound only for alpha-tested (foliage) materials
uniform int       uAlphaTest;  // 1 = discard by albedo alpha so GTAO sees the leaf cutout

// Tunable (FoliageConfig.AlphaCutoff). Must match shaderPBR.frag's/zprepass.frag's threshold
// exactly — see the BAYER_4X4 comment above.
uniform float uFoliageAlphaCutoff;

uniform sampler2D uRoughnessMap;
uniform sampler2D uMetallicMap;
uniform int   uHasRoughness;
uniform int   uHasMetallic;
uniform float uRoughnessValue;
uniform float uMetallicValue;

// ─────────────────────────────────────────────────────────────────────────────

float DitherThreshold(vec2 fragCoord)
{
    ivec2 p = ivec2(fragCoord) & 3;
    return (BAYER_4X4[p.y * 4 + p.x] + 0.5) / 16.0;
}

void main()
{
    // match the lit pass cutout exactly so the prepass depth/normals follow the actual leaf
    // shape, not the full quad — otherwise GTAO occludes from the transparent quad regions
    if (uAlphaTest == 1)
    {
        float a = texture(uAlbedo, fUv).a;
        if (a < uFoliageAlphaCutoff || a < DitherThreshold(gl_FragCoord.xy))
            discard;
    }

    vec3 n = normalize(vViewNormal);
    gNormal = vec4(n * 0.5 + 0.5, 1.0);

    float roughness = uHasRoughness == 1 ? texture(uRoughnessMap, fUv).r : uRoughnessValue;
    float metallic  = uHasMetallic  == 1 ? texture(uMetallicMap,  fUv).r : uMetallicValue;
    gMaterial = vec4(roughness, metallic, 0.0, 1.0);
}
