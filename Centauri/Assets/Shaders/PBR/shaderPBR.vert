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

// Sway weighted by height above the instance origin: the base stays planted, the canopy
// moves. Per-vertex phase from world XZ de-phases neighbouring leaves so they shimmer
// instead of sliding as a rigid block. Keep IDENTICAL in prepass.vert and depth.vert.
vec3 WindSway(vec3 worldPos, vec3 origin)
{
    const float AMP  = 0.06;
    const float FREQ = 1.6;
    const vec2  DIR  = vec2(0.8, 0.6);   // wind heading in world XZ

    float height = max(worldPos.y - origin.y, 0.0);
    float phase  = dot(worldPos.xz, vec2(0.35));
    float sway   = sin(uTime * FREQ + phase) + 0.5 * sin(uTime * FREQ * 2.3 + phase * 1.7);

    return worldPos + vec3(DIR.x, 0.0, DIR.y) * (sway * AMP * height);
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