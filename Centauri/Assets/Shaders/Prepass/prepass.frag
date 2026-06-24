#version 330 core

in vec3 vViewNormal;

layout (location = 0) out vec4 gNormal;   // view-space normal, encoded to [0,1]

void main()
{
    vec3 n = normalize(vViewNormal);
    gNormal = vec4(n * 0.5 + 0.5, 1.0);
}
