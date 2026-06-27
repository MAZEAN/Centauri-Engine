#version 330 core

out vec2 fUv;       // UV after scale/offset applied
out vec3 fNormal;   // world space normal
out vec3 fFragPos;  // world space position of this fragment
out mat3 fTBN;      // tangent space to world space matrix
out float fViewDepth;
out vec4 fClipPos;  // clip-space position, for screen-space lookups (SSAO)

layout (location = 0) in vec3 vPos;      // world position of vertex
layout (location = 1) in vec3 vNormal;   // surface direction at vertex
layout (location = 2) in vec2 vUv;       // texture coordinate
layout (location = 3) in vec3 vTangent;  // tangent direction, for normal mapping
layout (location = 4) in mat4 iModel;          // entity world transform (occupies 4..7)
layout (location = 8) in vec4 iUvScaleOffset;

uniform mat4 uView;         // camera transform — moves world relative to camera
uniform mat4 uProjection;   // perspective — makes far things smaller

uniform int   uWind;        // 1 = foliage sway (must match the prepass/depth passes)
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

    vec4 worldPos = iModel * vec4(vPos, 1.0);
    if (uWind == 1)
        worldPos.xyz = WindSway(worldPos.xyz, iModel[3].xyz);
    
    vec4 viewPos  = uView * worldPos;
    fViewDepth    = -viewPos.z;

    fUv         = vUv * iUvScaleOffset.xy + iUvScaleOffset.zw;
    fFragPos    = worldPos.xyz;

    vec3 T = normalize(normalMatrix * vTangent);
    vec3 N = normalize(normalMatrix * vNormal);
    T      = normalize(T - dot(T, N) * N); // re-orthogonalize
    vec3 B = cross(N, T);

    fTBN = mat3(T, B, N);
    fNormal = N;

    gl_Position  = uProjection * viewPos;
    fClipPos     = gl_Position;
}