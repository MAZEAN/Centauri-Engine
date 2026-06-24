#version 330 core

out vec3 vViewNormal;
out vec2 vUv;

layout (location = 0) in vec3 vPos;
layout (location = 1) in vec3 vNormal;
layout (location = 2) in vec2 vTexCoord;

uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProjection;
uniform mat3 uNormalMatrix;   // transpose(inverse(model)) — world-space normal basis

uniform vec2 uUvScale;
uniform vec2 uUvOffset;

void main()
{
    vec3 worldN = normalize(uNormalMatrix * vNormal);
    vViewNormal = mat3(uView) * worldN;             // world -> view space
    vUv         = vTexCoord * uUvScale + uUvOffset;

    gl_Position = uProjection * uView * uModel * vec4(vPos, 1.0);
}
