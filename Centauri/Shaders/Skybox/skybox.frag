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

    return color * intensity * (0.2 + 0.8 * sunUp);
}

void main()
{
    vec3 d = normalize(vDir);

    vec3 procedural = proceduralSky(d, uSunDir, uTurbidity, uSkyIntensity);
    float day = smoothstep(-0.08, 0.12, uSunDir.y);
    procedural = mix(vec3(0.008, 0.012, 0.025), procedural, day);

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