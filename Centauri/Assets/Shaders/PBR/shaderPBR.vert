#version 330 core

layout (location = 0) in vec3 vPos;      // world position of vertex
layout (location = 1) in vec3 vNormal;   // surface direction at vertex
layout (location = 2) in vec2 vUv;       // texture coordinate
layout (location = 3) in vec3 vTangent;  // tangent direction, for normal mapping

uniform mat4 uModel;        // entity's world transform (position/rotation/scale)
uniform mat4 uView;         // camera transform — moves world relative to camera
uniform mat4 uProjection;   // perspective — makes far things smaller
uniform vec2 uUvScale;      // texture tiling
uniform vec2 uUvOffset;     // texture offset
uniform mat3 uNormalMatrix; // transposed inverse of uModel

out vec2 fUv;       // UV after scale/offset applied
out vec3 fNormal;   // world space normal
out vec3 fFragPos;  // world space position of this fragment
out mat3 fTBN;      // tangent space to world space matrix

void main()
{
    gl_Position = uProjection * uView * uModel * vec4(vPos, 1.0);
    fUv         = vUv * uUvScale + uUvOffset;
    fFragPos    = vec3(uModel * vec4(vPos, 1.0));

    vec3 T = normalize(uNormalMatrix * vTangent);
    vec3 N = normalize(uNormalMatrix * vNormal);
    T      = normalize(T - dot(T, N) * N); // re-orthogonalize
    vec3 B = cross(N, T);

    fTBN = mat3(T, B, N);
    fNormal = N;
}