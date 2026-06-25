#version 330 core

out vec3 vViewNormal;

layout (location = 0) in vec3 vPos;
layout (location = 1) in vec3 vNormal;
layout (location = 4) in mat4 iModel;

uniform mat4 uView;
uniform mat4 uProjection;

void main()
{
    mat3 normalMatrix = transpose(inverse(mat3(iModel)));
    vec3 worldN = normalize(normalMatrix * vNormal);
    
    vViewNormal = mat3(uView) * worldN;             // world -> view space

    gl_Position = uProjection * uView * iModel * vec4(vPos, 1.0);
}
