#version 330 core

layout (location = 0) in vec3 aPos;   // Mesh stride is 11 floats; only position used

uniform mat4 uView;
uniform mat4 uProjection;

out vec3 vDir;

void main()
{
    vDir = aPos;
    gl_Position = uProjection * uView * vec4(aPos, 1.0);
}