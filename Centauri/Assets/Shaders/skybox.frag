#version 330 core

in  vec3 vDir;
out vec4 FragColor;

uniform samplerCube uSkybox;

void main()
{
    FragColor = texture(uSkybox, vDir);
}