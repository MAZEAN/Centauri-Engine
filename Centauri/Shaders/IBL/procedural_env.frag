#version 330 core

in  vec3 vLocalPos;
out vec4 FragColor;

const vec3 RAYLEIGH_WEIGHT = vec3(0.35, 0.55, 1.0);

uniform vec3  uSunDir;
uniform float uTurbidity;
uniform float uSkyIntensity;

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

    FragColor = vec4(color, 1.0);
}
