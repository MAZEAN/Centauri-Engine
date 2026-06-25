#version 330 core

layout (location = 0) in vec3 vPos;
layout (location = 4) in mat4 iModel;

uniform mat4 uLightMatrix;

void main() { 
    gl_Position = uLightMatrix * iModel * vec4(vPos, 1.0); 
}