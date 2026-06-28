#version 330 core

// SSR resolve blur. Blur radius is driven by surface roughness (mirror → 0, sharp; rougher →
//// wider). VALIDITY-WEIGHTED: each tap is weighted by the raw reflection's alpha (1 = hit,
//// 0 = miss) so black miss texels can't darken a hit.
////
//// COVERAGE-GATED: a pixel only keeps reflection if enough of its kernel actually hit. Without
//// this, the validity weighting fills miss pixels from a single valid neighbour, dilating
//// isolated/noisy hits into faint "flowing" wisps that leak past object silhouettes. The gate
//// drops those sparse bleeds while still smoothing solid glossy regions.

in  vec2 vUv;
out vec4 FragColor;

const int MAXR = 3;            // bounded kernel half-width
const float MIN_COVERAGE = 0.5;  // need >50% weighted hits to keep reflection

uniform sampler2D uSsr;        // raw reflection
uniform sampler2D uMaterial;   // r = roughness
uniform vec2  uTexel;          // 1 / size
uniform float uRoughnessCutoff;

void main()
{
    float roughness = texture(uMaterial, vUv).r;
    float radius    = clamp(roughness / max(uRoughnessCutoff, 1e-3), 0.0, 1.0) * float(MAXR);

    vec3  sum     = vec3(0.0);
    float wsum    = 0.0;   // validity-weighted (only hits)
    float totalW  = 0.0;   // all in-radius taps regardless of validity

    for (int x = -MAXR; x <= MAXR; x++)
    for (int y = -MAXR; y <= MAXR; y++)
    {
        float r = length(vec2(x, y));
        if (r > radius) continue;                       // per-pixel circular kernel

        float g = exp(-r * r / max(2.0 * radius * radius, 1e-3));
        vec4  s = texture(uSsr, vUv + vec2(x, y) * uTexel);

        sum    += s.rgb * g * s.a;
        wsum   += g * s.a;
        totalW += g;
    }

    // gate on how much of the kernel actually hit — drops sparse dilation wisps
    float coverage = totalW > 0.0 ? wsum / totalW : 0.0;
    if (coverage < MIN_COVERAGE) { FragColor = vec4(0.0); return; }

    FragColor = vec4(sum / wsum, coverage);
}