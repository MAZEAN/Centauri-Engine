#version 330 core

out vec3 vViewNormal;
out vec2 fUv;

layout (location = 0) in vec3 vPos;
layout (location = 1) in vec3 vNormal;
layout (location = 2) in vec2 vUv;
layout (location = 4) in mat4 iModel;   // per-instance world transform (occupies 4..7)

uniform mat4 uView;
uniform mat4 uProjection;

void main()
{
    mat3 normalMatrix = transpose(inverse(mat3(iModel)));
    vec3 worldN = normalize(normalMatrix * vNormal);

    vViewNormal = mat3(uView) * worldN;             // world -> view space
    fUv         = vUv;

    gl_Position = uProjection * uView * iModel * vec4(vPos, 1.0);
}
