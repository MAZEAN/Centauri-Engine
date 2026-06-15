#version 330 core

const vec2 invAtan = vec2(0.1591549, 0.3183099); // 1/(2π), 1/π

uniform sampler2D uPanorama;
uniform int   uHdr;        // 1 = linear HDR radiance, 0 = display-ready sRGB LDR
uniform float uExposure;   // pre-tonemap multiplier (HDR only)

in  vec3 vDir;
out vec4 FragColor;

// Narkowicz 2015 ACES filmic approximation — keeps bright highlights (moon,
// stars, sun) from clipping while preserving the deep, smooth shadow gradients
// that 8-bit PNGs band so badly on night skies.
vec3 ACESFilm(vec3 x)
{
    const float a = 2.51, b = 0.03, c = 2.43, d = 0.59, e = 0.14;
    return clamp((x * (a * x + b)) / (x * (c * x + d) + e), 0.0, 1.0);
}

void main()
{
    vec3 d  = normalize(vDir);
    vec2 uv = vec2(atan(d.z, d.x), asin(clamp(d.y, -1.0, 1.0))) * invAtan + 0.5;
    vec3 color = textureLod(uPanorama, uv, 0.0).rgb;   // mip 0 → no blurry wrap-seam

    if (uHdr == 1)
    {
        color = ACESFilm(color * uExposure);           // tonemap linear radiance
        color = pow(color, vec3(1.0 / 2.2));           // → sRGB, matching the PBR pass
    }

    FragColor = vec4(color, 1.0);
}