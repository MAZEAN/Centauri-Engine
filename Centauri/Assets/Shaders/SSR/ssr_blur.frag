#version 330 core

// SSR resolve blur. Single ray per pixel is inherently noisy/banded, and glossy surfaces
// should reflect more softly than mirrors. Blur the raw reflection with a radius driven by
// the surface roughness: mirror (roughness 0) → radius 0 → untouched/sharp; rougher → wider
// Gaussian. This both denoises the march and approximates roughness-correct reflection blur.

in  vec2 vUv;
out vec4 FragColor;

uniform sampler2D uSsr;        // raw reflection
uniform sampler2D uMaterial;   // r = roughness
uniform vec2  uTexel;          // 1 / size
uniform float uRoughnessCutoff;

const int MAXR = 3;            // bounded kernel half-width

void main()
{
    float roughness = texture(uMaterial, vUv).r;
    float radius    = clamp(roughness / max(uRoughnessCutoff, 1e-3), 0.0, 1.0) * float(MAXR);

    vec4  sum  = vec4(0.0);
    float wsum = 0.0;

    for (int x = -MAXR; x <= MAXR; x++)
    for (int y = -MAXR; y <= MAXR; y++)
    {
        float r = length(vec2(x, y));
        if (r > radius) continue;                       // per-pixel circular kernel
        float w = exp(-r * r / max(2.0 * radius * radius, 1e-3));
        sum  += texture(uSsr, vUv + vec2(x, y) * uTexel) * w;
        wsum += w;
    }

    FragColor = wsum > 0.0 ? sum / wsum : texture(uSsr, vUv);
}
