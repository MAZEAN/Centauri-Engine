#version 330 core

out vec3 vViewNormal;
out vec2 fUv;

layout (location = 0) in vec3 vPos;
layout (location = 1) in vec3 vNormal;
layout (location = 2) in vec2 vUv;
layout (location = 4) in mat4 iModel;   // per-instance world transform (occupies 4..7)

uniform mat4 uView;
uniform mat4 uProjection;

uniform int   uWind;        // 1 = foliage sway (must match the lit/depth passes)
uniform float uTime;        // seconds, latched once per frame

uniform float uWindStrength;   // sway amplitude   (WindConfig.Strength)
uniform float uWindSpeed;      // oscillation freq (WindConfig.Speed)
uniform vec2  uWindDir;

vec3 WindSway(vec3 worldPos, vec3 origin)
{
    float height = max(worldPos.y - origin.y, 0.0);
    float phase  = dot(worldPos.xz, vec2(0.35));
    float sway   = sin(uTime * uWindSpeed + phase) + 0.5 * sin(uTime * uWindSpeed * 2.3 + phase * 1.7);

    return worldPos + vec3(uWindDir.x, 0.0, uWindDir.y) * (sway * uWindStrength * height);
}

void main()
{
    mat3 normalMatrix = transpose(inverse(mat3(iModel)));
    vec3 worldN = normalize(normalMatrix * vNormal);

    vViewNormal = mat3(uView) * worldN;             // world -> view space
    fUv         = vUv;

    vec4 worldPos = iModel * vec4(vPos, 1.0);
    if (uWind == 1)
        worldPos.xyz = WindSway(worldPos.xyz, iModel[3].xyz);

    gl_Position = uProjection * uView * worldPos;
}
