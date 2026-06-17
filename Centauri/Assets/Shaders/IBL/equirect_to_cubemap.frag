#version 330 core

in vec3 vLocalPos;
out vec4 FragColor;

const vec2 invAtan = vec2(0.1591549, 0.3183099);
const float maxVal = 65504.0;

uniform sampler2D uEquirect;
uniform float uExposure;

void main() {
    vec3 v = normalize(vLocalPos);
    vec2 uv = vec2(atan(v.z, v.x), asin(clamp(v.y, -1.0, 1.0))) * invAtan + 0.5;

    vec3 c = texture(uEquirect, uv).rgb * uExposure;
    c = min(c, vec3(maxVal));   // .hdr sun overflows RGB16F to +Inf — clamp finite so IBL stays NaN-free
    FragColor = vec4(c, 1.0);
}