#version 330 core

in vec3 vLocalPos;
out vec4 FragColor;

const vec2 invAtan = vec2(0.1591549, 0.3183099);

uniform sampler2D uEquirect;

void main() {
    vec3 v = normalize(vLocalPos);
    vec2 uv = vec2(atan(v.z, v.x), asin(clamp(v.y, -1.0, 1.0))) * invAtan + 0.5;
    FragColor = vec4(texture(uEquirect, uv).rgb, 1.0);
}