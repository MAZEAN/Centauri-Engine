#version 330 core

out vec2 fUv;

layout (location = 0) in vec3 vPos;
layout (location = 2) in vec2 vUv;
layout (location = 4) in mat4 iModel;

uniform mat4 uLightMatrix;

void main() {
    fUv = vUv;
    gl_Position = uLightMatrix * iModel * vec4(vPos, 1.0); 
}