#version 330 core

in  vec3 vLocalPos;
out vec4 FragColor;

const vec3 RAYLEIGH_WEIGHT = vec3(0.35, 0.55, 1.0);

uniform vec3  uSunDir;
uniform float uTurbidity;
uniform float uSkyIntensity;

uniform float uCloudCoverage;
uniform float uCloudScale;
uniform float uCloudSpeed;
uniform float uCloudShading;
uniform float uTime;

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

float fbm(vec3 p)
{
    float sum = 0.0;
    float amp = 0.5;
    for (int i = 0; i < 5; i++)
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

vec3 applyClouds(vec3 skyColor, vec3 dir, vec3 sunDir,
        float coverage, float scale, float speed, float shading, float time)
{
    if (coverage <= 0.0)
    return skyColor;

    vec3 wind   = vec3(0.7, 0.3, 0.5) * time * speed;
    vec3 p      = dir * scale + wind;
    float shape = fbm(p);

    float erosion = fbm(p * 4.0 + vec3(11.1, 3.3, 9.9));
    float eroded  = shape - erosion * 0.18;

    float threshold = mix(0.75, 0.05, clamp(coverage, 0.0, 1.0));
    float density   = smoothstep(threshold, threshold + 0.05, eroded);
    density *= smoothstep(-0.05, 0.1, dir.y);

    if (density <= 0.001) return skyColor;

    float thickness = clamp((eroded - threshold) / 0.5, 0.0, 1.0);

    float detail = fbm(p * 3.0 + vec3(5.2, 1.3, 7.8));

    float e  = 0.2;
    float nx = fbmLite(p + vec3(e, 0.0, 0.0)) - fbmLite(p - vec3(e, 0.0, 0.0));
    float ny = fbmLite(p + vec3(0.0, e, 0.0)) - fbmLite(p - vec3(0.0, e, 0.0));
    float nz = fbmLite(p + vec3(0.0, 0.0, e)) - fbmLite(p - vec3(0.0, 0.0, e));
    vec3  cloudNormal = normalize(vec3(-nx, -ny, -nz) * 4.0 + vec3(0.0, 0.3, 0.0));
    float wrap = clamp(dot(cloudNormal, sunDir) * 0.5 + 0.5, 0.0, 1.0);

    float rawShade = mix(0.35, 1.0, thickness) * mix(0.8, 1.0, detail) * mix(0.75, 1.0, wrap);
    float shd      = clamp(mix(1.0, rawShade, shading), 0.0, 1.0);
    vec3  base     = mix(vec3(0.35, 0.38, 0.45), vec3(0.95, 0.95, 0.98), shd);

    float sunFacing = pow(clamp(dot(dir, sunDir), 0.0, 1.0), 2.0);
    float sunLow    = 1.0 - smoothstep(0.0, 0.35, clamp(sunDir.y, 0.0, 1.0));
    vec3  warmTint  = mix(vec3(1.0), vec3(1.0, 0.55, 0.3), sunLow);
    vec3  cloudColor = base * warmTint * (1.0 + sunFacing * 0.3);

    return mix(skyColor, cloudColor, density);
}

vec3 proceduralSky(vec3 dir, vec3 sunDir, float turbidity, float intensity)
{
    float sunUp   = clamp(sunDir.y, 0.0, 1.0);
    float cosView = max(dir.y, 0.02);              // avoid dividing by ~0 at the horizon
    float opticalDepth = 1.0 / cosView;             // thicker atmosphere path near the horizon

    vec3 extinction = exp(-RAYLEIGH_WEIGHT * turbidity * 0.15 * opticalDepth);
    vec3 rayleigh   = vec3(1.0) - extinction;

    float cosTheta = dot(dir, sunDir);
    float mie = pow(clamp(cosTheta, 0.0, 1.0), 8.0);

    vec3 color = rayleigh * RAYLEIGH_WEIGHT + mie * vec3(1.0, 0.85, 0.65) * 0.3;
    color *= (0.2 + 0.8 * sunUp);

    float sunLow  = 1.0 - smoothstep(0.0, 0.35, sunUp);
    float grazing = 1.0 - clamp(dir.y, 0.0, 1.0);
    float sunSide = 0.4 + 0.6 * clamp(cosTheta, 0.0, 1.0);
    color += vec3(1.0, 0.45, 0.2) * (sunLow * grazing * sunSide) * 0.6;

    return color * intensity;
}

void main()
{
    vec3 dir = normalize(vLocalPos);
    vec3 color = proceduralSky(dir, uSunDir, uTurbidity, uSkyIntensity);

    float day = smoothstep(-0.08, 0.12, uSunDir.y);
    color = mix(vec3(0.008, 0.012, 0.025), color, day);

    color = applyClouds(color, dir, uSunDir, uCloudCoverage, uCloudScale, uCloudSpeed, uCloudShading, uTime);

    FragColor = vec4(color, 1.0);
}