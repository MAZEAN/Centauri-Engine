#version 330 core

out vec2 fUv;

layout (location = 0) in vec3 vPos;
layout (location = 2) in vec2 vUv;
layout (location = 4) in mat4 iModel;

uniform mat4 uLightMatrix;

uniform int   uWind;        // 1 = foliage sway (must match the lit/prepass passes)
uniform float uTime;        // seconds, latched once per frame

uniform float uWindStrength;   // sway amplitude
uniform float uWindSpeed;      // oscillation freq
uniform vec2  uWindDir;

vec3 WindSway(vec3 worldPos, vec3 origin)
{
    vec2 windDir = normalize(uWindDir);

    float h = clamp(worldPos.y - origin.y, 0.0, 1.0);
    float bendWeight = h * h;

    float seed = fract(sin(dot(origin.xz, vec2(12.9898, 78.233))) * 43758.5453);

    float phase = dot(worldPos.xz, windDir) * 0.4 + seed * 6.28318;

    float gust = 0.7 + 0.3 * sin(uTime * 0.15) + 0.15 * sin(uTime * 0.07 + seed * 3.0);

    float sway = sin(uTime * uWindSpeed + phase)
                + 0.4 * sin(uTime * uWindSpeed * 2.1 + phase * 2.7)
                + 0.2 * sin(uTime * uWindSpeed * 4.7 + phase * 5.1);

    float amount = sway * gust * uWindStrength * bendWeight;

    worldPos.xz += windDir * amount;
    worldPos.y -= abs(amount) * 0.1;

    return worldPos;
}

void main() {
    fUv = vUv;
    
    vec4 worldPos = iModel * vec4(vPos, 1.0);
    if (uWind == 1)
        worldPos.xyz = WindSway(worldPos.xyz, iModel[3].xyz);

    gl_Position = uLightMatrix * worldPos;
}