#version 330 core

in  vec3 vLocalPos;
out vec4 FragColor;

uniform vec3  uSunDir;
uniform float uTurbidity;
uniform float uSkyIntensity;

// Same bounded Rayleigh/Mie approximation as Shaders/Skybox/skybox.frag's proceduralSky —
// this project has no shared-include mechanism between shader files, so keep any tuning
// changes mirrored between the two by hand. Deliberately excludes the sun disc itself: baking
// a tiny, extremely bright feature into a low-res irradiance/prefilter cubemap would either
// alias badly or blow out the convolution — direct sun lighting already comes from the
// DirectionalLight, this only needs the ambient sky around it.
const vec3 RAYLEIGH_WEIGHT = vec3(0.35, 0.55, 1.0);

vec3 proceduralSky(vec3 dir, vec3 sunDir, float turbidity, float intensity)
{
    float sunUp   = clamp(sunDir.y, 0.0, 1.0);
    float cosView = max(dir.y, 0.02);
    float opticalDepth = 1.0 / cosView;

    vec3 extinction = exp(-RAYLEIGH_WEIGHT * turbidity * 0.15 * opticalDepth);
    vec3 rayleigh   = vec3(1.0) - extinction;

    float cosTheta = dot(dir, sunDir);
    float mie = pow(clamp(cosTheta, 0.0, 1.0), 8.0);

    vec3 color = rayleigh * RAYLEIGH_WEIGHT * 2.0 + mie * vec3(1.0, 0.85, 0.65) * 0.5;

    return color * intensity * (0.2 + 0.8 * sunUp);
}

void main()
{
    vec3 dir = normalize(vLocalPos);
    vec3 color = proceduralSky(dir, uSunDir, uTurbidity, uSkyIntensity);

    float day = smoothstep(-0.08, 0.12, uSunDir.y);
    color = mix(vec3(0.008, 0.012, 0.025), color, day);

    FragColor = vec4(color, 1.0);
}
