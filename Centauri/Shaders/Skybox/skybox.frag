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
uniform float uTime;           // seconds, for scrolling

// ── Cheap value-noise fBm, self-contained (no texture, no built-in noise in GLSL 330) ──
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

// 5 octaves, amplitudes halving — bounded to [0, ~0.97] regardless of input.
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

// Flat cloud layer sampled in the same equirect UV space used for the textured panorama —
// avoids a ground-plane projection's blowup at grazing angles, at the cost of mild pinching
// at the zenith (masked below by zenithFade, since that's also the least-looked-at part of
// the dome). Bounded by construction: fbm is capped, smoothstep/clamp keep every factor in
// [0,1], so coverage/scale/speed can never push this past the base sky's own brightness.
vec3 applyClouds(vec3 skyColor, vec2 uv, vec3 dir, vec3 sunDir,
        float coverage, float scale, float speed, float time)
{
    if (coverage <= 0.0) 
        return skyColor;   // clouds off — skip the noise entirely

    vec2  p = uv * scale * vec2(2.0, 1.0) + vec2(0.7, 0.3) * time * speed;
    float n = fbm(p);

    // Higher coverage lowers the threshold, so more of the noise field counts as "cloud".
    float threshold = mix(0.75, 0.05, clamp(coverage, 0.0, 1.0));
    float density   = smoothstep(threshold, threshold + 0.15, n);

    float horizonFade = smoothstep(-0.05, 0.1, dir.y);        // no clouds at/below the horizon
    float zenithFade   = 1.0 - smoothstep(0.85, 1.0, dir.y);  // hide the equirect pole pinch
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
    vec3 d = normalize(vDir);

    vec3 procedural = proceduralSky(d, uSunDir, uTurbidity, uSkyIntensity);
    float day = smoothstep(-0.08, 0.12, uSunDir.y);
    procedural = mix(vec3(0.008, 0.012, 0.025), procedural, day);

    vec2 uv = vec2(atan(d.z, d.x), asin(clamp(d.y, -1.0, 1.0))) * invAtan + 0.5;
    
    procedural = applyClouds(procedural, uv, d, uSunDir, uCloudCoverage, uCloudScale, uCloudSpeed, uTime);
    
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