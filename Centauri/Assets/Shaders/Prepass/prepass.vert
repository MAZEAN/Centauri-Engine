#version 330 core

out vec3 vViewNormal;

layout (location = 0) in vec3 vPos;
layout (location = 1) in vec3 vNormal;

uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProjection;
uniform mat3 uNormalMatrix;   // transpose(inverse(model)) — world-space normal basis

void main()
{
    vec3 worldN = normalize(uNormalMatrix * vNormal);
    vViewNormal = mat3(uView) * worldN;             // world -> view space

    gl_Position = uProjection * uView * uModel * vec4(vPos, 1.0);
}
