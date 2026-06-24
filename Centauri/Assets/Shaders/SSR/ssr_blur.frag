#version 330 core

// SSR resolve blur. Single ray per pixel gives a noisy/banded result with a hard, jagged
// hit/miss coverage edge. Blur with a radius driven by surface roughness (mirror → tight,
// rough → wide). Crucially this is a VALIDITY-WEIGHTED blur: each tap is weighted by the
// raw reflection's alpha (1 = hit, 0 = miss), so black miss texels never darken a hit and
// valid reflection bleeds across the jagged coverage boundary to soften it.

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
    float radius    = mix(1.0, float(MAXR), clamp(roughness / max(uRoughnessCutoff, 1e-3), 0.0, 1.0));

    vec3  sum  = vec3(0.0);
    float wsum = 0.0;

    for (int x = -MAXR; x <= MAXR; x++)
        for (int y = -MAXR; y <= MAXR; y++)
        {
            float r = length(vec2(x, y));
            if (r > radius) continue;                       // per-pixel circular kernel
            
            vec4  s = texture(uSsr, vUv + vec2(x, y) * uTexel);
            float w = exp(-r * r / max(2.0 * radius * radius, 1e-3)) * s.a;   // weight by validity
            
            sum  += s.rgb * w;
            wsum += w;
        }

    FragColor = wsum > 0.0 ? vec4(sum / wsum, 1.0) : vec4(0.0);
}
