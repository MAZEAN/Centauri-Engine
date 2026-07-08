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

uniform float uWindStrength;   // sway amplitude
uniform float uWindSpeed;      // oscillation freq
uniform vec2  uWindDir;

vec3 WindSway(vec3 worldPos, vec3 origin)
{
    vec2 windDir = normalize(uWindDir);

    float h = clamp(worldPos.y - origin.y, 0.0, 1.0);
    float bendWeight = h * h;

    float seed = fract(sin(dot(origin.xz, vec2(12.9898, 78.233))) * 43758.5453);

    // High-frequency hash on the vertex's own world position (not just the tree's origin) so
    // individual leaf cards desync from each other instead of the whole canopy moving as one
    // rigid gradient. Nearby vertices on the same small leaf card land on nearly the same
    // value (still internally coherent), but neighboring cards a short distance apart land on
    // a very different phase/amplitude. Must stay identical across shaderPBR.vert/prepass.vert/
    // depth.vert — see uWind's comment.
    float leafSeed = fract(sin(dot(worldPos.xyz, vec3(269.5, 183.3, 311.7))) * 43758.5453);
    float leafAmp  = 0.7 + 0.3 * leafSeed;
    float phase = dot(worldPos.xz, windDir) * 0.4 + seed * 6.28318 + leafSeed * 6.28318;
    
    float gust = 0.7 + 0.3 * sin(uTime * 0.15) + 0.15 * sin(uTime * 0.07 + seed * 3.0);

    float sway = sin(uTime * uWindSpeed + phase)
                + 0.4 * sin(uTime * uWindSpeed * 2.1 + phase * 2.7)
                + 0.2 * sin(uTime * uWindSpeed * 4.7 + phase * 5.1);

    float amount = sway * gust * uWindStrength * bendWeight * leafAmp;

    worldPos.xz += windDir * amount;
    worldPos.y -= abs(amount) * 0.1;

    return worldPos;
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