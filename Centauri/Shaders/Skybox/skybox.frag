#version 330 core

in  vec3 vDir;

out vec4 FragColor;

const vec2 invAtan = vec2(0.1591549, 0.3183099); // 1/(2π), 1/π
const vec3 RAYLEIGH_WEIGHT = vec3(0.35, 0.55, 1.0);

uniform sampler2D uPanorama;
uniform int   uHdr;         // 1 = linear HDR radiance, 0 = display-ready sRGB LDR
uniform float uExposure;    // pre-tonemap multiplier (HDR only)
uniform float uBlackLevel;  // crush radiance below this to black (HDR only)

uniform float uProceduralBlend;  // 0 = pure panorama, 1 = pure procedural atmosphere

uniform vec3  uSunDir;          // world-space direction from the sky toward the sun
uniform vec3  uSunColor;        // sun disc radiance
uniform float uSunAngularSize;  // cosine of the disc's half-angle
uniform float uSunGlowExponent; // higher = tighter halo around the disc

// Procedural sky
uniform float uTurbidity;   // atmospheric haziness (1 clear .. 6+ hazy)
uniform float uSkyIntensity; // scales relative sky radiance into the exposure/tonemap range

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

vec3 applyClouds(vec3 skyColor, vec3 dir, vec3 sunDir, float coverage, float scale, float speed, float shading, float time)
{
    if (coverage <= 0.0)
        return skyColor;   // clouds off — skip the noise entirely

    vec3 wind   = vec3(0.7, 0.3, 0.5) * time * speed;
    vec3 p      = dir * scale + wind;
    float shape = fbm(p);

    float erosion = fbm(p * 4.0 + vec3(11.1, 3.3, 9.9));
    float eroded  = shape - erosion * 0.18;

    // Higher coverage lowers the threshold, so more of the noise field counts as "cloud".
    float threshold = mix(0.75, 0.05, clamp(coverage, 0.0, 1.0));
    float density   = smoothstep(threshold, threshold + 0.05, eroded);
    density *= smoothstep(-0.05, 0.1, dir.y);   // no clouds at/below the horizon

    if (density <= 0.001) 
        return skyColor;   // clear here — skip the shading work below
    
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
    vec3 d = normalize(vDir);

    vec3 procedural = proceduralSky(d, uSunDir, uTurbidity, uSkyIntensity);
    float day = smoothstep(-0.08, 0.12, uSunDir.y);
    procedural = mix(vec3(0.008, 0.012, 0.025), procedural, day);

    procedural = applyClouds(procedural, d, uSunDir, uCloudCoverage, uCloudScale, uCloudSpeed, uCloudShading, uTime);

    vec2 uv = vec2(atan(d.z, d.x), asin(clamp(d.y, -1.0, 1.0))) * invAtan + 0.5;
    vec3 textured = texture(uPanorama, uv).rgb;

    if (uHdr == 1)
    textured *= uExposure;
    else
    textured = pow(textured, vec3(2.2));
    textured = max(textured - uBlackLevel, vec3(0.0));

    vec3 color = mix(textured, procedural, uProceduralBlend);

    float cosAngle = dot(d, uSunDir);
    float disc     = smoothstep(uSunAngularSize - 0.0006, uSunAngularSize, cosAngle);
    float glow     = pow(max(cosAngle, 0.0), uSunGlowExponent);
    color += uSunColor * (disc * 6.0 + glow * 0.4);

    FragColor = vec4(color, 1.0);       // linear HDR — global grade + tonemap happen in post
}