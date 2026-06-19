#version 330 core

layout (location = 0) in vec3 vPos;

uniform mat4 uModel;
uniform mat4 uLightView;
uniform mat4 uLightProjection;

void main()
{
    gl_Position = uLightProjection * uLightView * uModel * vec4(vPos, 1.0);
}