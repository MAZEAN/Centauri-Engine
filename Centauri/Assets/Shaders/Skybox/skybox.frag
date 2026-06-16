#version 330 core

const vec2 invAtan = vec2(0.1591549, 0.3183099); // 1/(2π), 1/π
const float maxVal = 65504.0;

uniform sampler2D uPanorama;
uniform int   uHDR;         // 1 = linear HDR radiance, 0 = display-ready sRGB LDR
uniform float uExposure;    // pre-tonemap multiplier (HDR only)
uniform float uBlackLevel;  // crush radiance below this to black (HDR only)

in  vec3 vDir;
out vec4 FragColor;

vec3 ACESFilm(vec3 x)
{
    const float a = 2.51, b = 0.03, c = 2.43, d = 0.59, e = 0.14;
    return clamp((x * (a * x + b)) / (x * (c * x + d) + e), 0.0, 1.0);
}

void main()
{
    vec3 d  = normalize(vDir);
    vec2 uv = vec2(atan(d.z, d.x), asin(clamp(d.y, -1.0, 1.0))) * invAtan + 0.5;
    vec3 color = textureLod(uPanorama, uv, 0.0).rgb;
    
    if (uHDR == 1)
        color *= uExposure;            // HDR: linear radiance, just normalize brightness
    else
        color = pow(color, vec3(2.2)); // LDR sRGB → linear so the post pass grades it too
        FragColor = vec4(color, 1.0);

    FragColor = vec4(color, 1.0);
}