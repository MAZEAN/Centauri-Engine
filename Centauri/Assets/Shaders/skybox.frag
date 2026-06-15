#version 330 core

const vec2 invAtan = vec2(0.1591549, 0.3183099); // 1/(2π), 1/π

uniform sampler2D uPanorama;

in  vec3 vDir;
out vec4 FragColor;

void main()
{
    vec3 d  = normalize(vDir);
    vec2 uv = vec2(atan(d.z, d.x), asin(clamp(d.y, -1.0, 1.0))) * invAtan + 0.5;
    FragColor = textureLod(uPanorama, uv, 0.0);   // mip 0 → no blurry wrap-seam
}