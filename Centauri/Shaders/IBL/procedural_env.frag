#version 330 core

in  vec3 vLocalPos;
out vec4 FragColor;

const vec3 RAYLEIGH_WEIGHT = vec3(0.35, 0.55, 1.0);
const vec2 invAtan = vec2(0.1591549, 0.3183099);

uniform vec3  uSunDir;
uniform float uTurbidity;
uniform float uSkyIntensity;

uniform float uCloudCoverage;
uniform float uCloudScale;
uniform float uCloudSpeed;
uniform float uTime;

float hash(vec2 p)
{
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

float valueNoise(vec2 p)
{
    vec2 i = floor(p);
    vec2 f = fract(p);
    float a = hash(i);
    float b = hash(i + vec2(1.0, 0.0));
    float c = hash(i + vec2(0.0, 1.0));
    float d = hash(i + vec2(1.0, 1.0));
    vec2  u = f * f * (3.0 - 2.0 * f);
    return mix(a, b, u.x) + (c - a) * u.y * (1.0 - u.x) + (d - b) * u.x * u.y;
}

float fbm(vec2 p)
{
    float sum = 0.0;
    float amp = 0.5;
    for (int i = 0; i < 5; i++)
    {
        sum += amp * valueNoise(p);
        p   *= 2.0;
        amp *= 0.5;
    }
    return sum;
}

// Clouds baked straight into the IBL source env — smooth and directionally broad (unlike the
// sun disc), so unlike that it's safe to convolve: coverage now affects ambient/specular
// lighting the same way it affects the visible sky. Mirrors skybox.frag's applyClouds().
vec3 applyClouds(vec3 skyColor, vec2 uv, vec3 dir, vec3 sunDir,
        float coverage, float scale, float speed, float time)
{
    if (coverage <= 0.0) 
        return skyColor;

    vec2  p = uv * scale * vec2(2.0, 1.0) + vec2(0.7, 0.3) * time * speed;
    float n = fbm(p);

    float threshold = mix(0.75, 0.05, clamp(coverage, 0.0, 1.0));
    float density   = smoothstep(threshold, threshold + 0.15, n);

    float horizonFade = smoothstep(-0.05, 0.1, dir.y);
    float zenithFade   = 1.0 - smoothstep(0.85, 1.0, dir.y);
    density *= horizonFade * zenithFade;

    float sunFacing = pow(clamp(dot(dir, sunDir), 0.0, 1.0), 2.0);
    float sunLow    = 1.0 - smoothstep(0.0, 0.35, clamp(sunDir.y, 0.0, 1.0));
    vec3  warmTint  = mix(vec3(1.0), vec3(1.0, 0.55, 0.3), sunLow);
    vec3  cloudColor = vec3(0.75, 0.76, 0.8) * warmTint * (1.0 + sunFacing * 0.3);

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

    vec3 color = rayleigh * RAYLEIGH_WEIGHT * 2.0 + mie * vec3(1.0, 0.85, 0.65) * 0.5;
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

    vec2 uv = vec2(atan(dir.z, dir.x), asin(clamp(dir.y, -1.0, 1.0))) * invAtan + 0.5;
    color = applyClouds(color, uv, dir, uSunDir, uCloudCoverage, uCloudScale, uCloudSpeed, uTime);

    FragColor = vec4(color, 1.0);
}
