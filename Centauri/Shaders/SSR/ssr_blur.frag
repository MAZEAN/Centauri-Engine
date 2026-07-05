#version 330 core

in  vec2 vUv;
out vec4 FragColor;

const int   MAXR       = 3;      // bounded kernel half-width
const float MIN_RADIUS = 1.5;    // floor — covers the 4 direct + 4 diagonal neighbors
const float MIN_COVERAGE = 0.5;  // need >50% weighted hits to keep reflection

uniform sampler2D uSsr;        // raw reflection
uniform sampler2D uMaterial;   // r = roughness
uniform vec2  uTexel;          // 1 / size
uniform float uRoughnessCutoff;

void main()
{
    float roughness = texture(uMaterial, vUv).r;
    float radius    = max(MIN_RADIUS, clamp(roughness / max(uRoughnessCutoff, 1e-3), 0.0, 1.0) * float(MAXR));

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
    float gate = smoothstep(MIN_COVERAGE * 0.6, MIN_COVERAGE, coverage);
    if (gate <= 0.0) 
    { 
        FragColor = vec4(0.0);
        return; 
    }

    FragColor = vec4(sum / wsum, coverage * gate);
}