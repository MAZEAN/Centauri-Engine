#version 330 core

// Renders only the cloud layer (color + density in alpha) using the same direction-mapping
// cube + vertex shader as Skybox/skybox.vert. Drawn into a half-resolution offscreen target
// and sampled back (bilinear-upscaled) by skybox.frag — clouds are soft, low-frequency shapes,
// so the resolution loss is invisible while the repeated fbm noise evaluations drop ~4x.

in  vec3 vDir;
out vec4 FragColor;

uniform vec3  uSunDir;
uniform float uCloudCoverage;  // 0 = none (skipped entirely), 1 = fully overcast
uniform float uCloudScale;     // noise frequency — higher = smaller, more numerous clouds
uniform float uCloudSpeed;     // scroll speed
uniform float uCloudShading;   // shading contrast: 0 = flat cutout, 1 = full effect, >1 = harder
uniform float uTime;           // seconds, for scrolling

float hash3(vec3 p)
{
    p = fract(p * vec3(443.897, 441.423, 437.195));
    p += dot(p, p.yzx + 19.19);

    return fract((p.x + p.y) * p.z);
}

float valueNoise3(vec3 p)
{
    vec3 i = floor(p);
    vec3 f = fract(p);
    vec3 u = f * f * (3.0 - 2.0 * f);

    float n000 = hash3(i + vec3(0.0, 0.0, 0.0));
    float n100 = hash3(i + vec3(1.0, 0.0, 0.0));
    float n010 = hash3(i + vec3(0.0, 1.0, 0.0));
    float n110 = hash3(i + vec3(1.0, 1.0, 0.0));
    float n001 = hash3(i + vec3(0.0, 0.0, 1.0));
    float n101 = hash3(i + vec3(1.0, 0.0, 1.0));
    float n011 = hash3(i + vec3(0.0, 1.0, 1.0));
    float n111 = hash3(i + vec3(1.0, 1.0, 1.0));

    float nx00 = mix(n000, n100, u.x);
    float nx10 = mix(n010, n110, u.x);
    float nx01 = mix(n001, n101, u.x);
    float nx11 = mix(n011, n111, u.x);

    float nxy0 = mix(nx00, nx10, u.y);
    float nxy1 = mix(nx01, nx11, u.y);

    return mix(nxy0, nxy1, u.z);
}

// 4 octaves (was 5) — the 5th contributed under 3% of the total amplitude, so dropping it
// is imperceptible while cutting a fifth of every fbm() call's cost.
float fbm(vec3 p)
{
    float sum = 0.0;
    float amp = 0.5;
    for (int i = 0; i < 4; i++)
    {
        sum += amp * valueNoise3(p);
        p   *= 2.0;
        amp *= 0.5;
    }
    return sum;
}

float fbmLite(vec3 p)
{
    float sum = 0.0;
    float amp = 0.5;
    for (int i = 0; i < 2; i++)
    {
        sum += amp * valueNoise3(p);
        p   *= 2.0;
        amp *= 0.5;
    }
    return sum;
}

void main()
{
    if (uCloudCoverage <= 0.0)
    {
        FragColor = vec4(0.0);   // clouds off — skip the noise entirely
        return;
    }

    vec3 dir = normalize(vDir);

    vec3 wind   = vec3(0.7, 0.3, 0.5) * uTime * uCloudSpeed;
    vec3 p      = dir * uCloudScale + wind;
    float shape = fbm(p);

    float erosion = fbm(p * 4.0 + vec3(11.1, 3.3, 9.9));
    float eroded  = shape - erosion * 0.18;

    // Higher coverage lowers the threshold, so more of the noise field counts as "cloud".
    float threshold = mix(0.75, 0.05, clamp(uCloudCoverage, 0.0, 1.0));
    float density   = smoothstep(threshold, threshold + 0.05, eroded);
    density *= smoothstep(-0.05, 0.1, dir.y);   // no clouds at/below the horizon

    if (density <= 0.001)
    {
        FragColor = vec4(0.0);   // clear here — skip the shading work below
        return;
    }

    float thickness = clamp((eroded - threshold) / 0.5, 0.0, 1.0);

    float detail = fbm(p * 3.0 + vec3(5.2, 1.3, 7.8));

    float e  = 0.2;
    float nx = fbmLite(p + vec3(e, 0.0, 0.0)) - fbmLite(p - vec3(e, 0.0, 0.0));
    float ny = fbmLite(p + vec3(0.0, e, 0.0)) - fbmLite(p - vec3(0.0, e, 0.0));
    float nz = fbmLite(p + vec3(0.0, 0.0, e)) - fbmLite(p - vec3(0.0, 0.0, e));
    vec3  cloudNormal = normalize(vec3(-nx, -ny, -nz) * 4.0 + vec3(0.0, 0.3, 0.0));
    float wrap = clamp(dot(cloudNormal, uSunDir) * 0.5 + 0.5, 0.0, 1.0);

    float rawShade = mix(0.35, 1.0, thickness) * mix(0.8, 1.0, detail) * mix(0.75, 1.0, wrap);
    float shd      = clamp(mix(1.0, rawShade, uCloudShading), 0.0, 1.0);
    vec3  base     = mix(vec3(0.35, 0.38, 0.45), vec3(0.95, 0.95, 0.98), shd);

    float sunFacing = pow(clamp(dot(dir, uSunDir), 0.0, 1.0), 2.0);
    float sunLow    = 1.0 - smoothstep(0.0, 0.35, clamp(uSunDir.y, 0.0, 1.0));
    vec3  warmTint  = mix(vec3(1.0), vec3(1.0, 0.55, 0.3), sunLow);
    vec3  cloudColor = base * warmTint * (1.0 + sunFacing * 0.3);

    FragColor = vec4(cloudColor, density);
}
