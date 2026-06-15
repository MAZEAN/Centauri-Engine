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
    vec3 color = texture(uPanorama, uv).rgb;   // mip 0 → no blurry wrap-seam

    if (uHDR == 1)
    {
        // Bright sun texels overflow the RGB16F texture to +Inf; ACES then
        // yields NaN, which clamps to black — the black square at the sun.
        // Clamping to a finite ceiling first keeps everything well-defined.
        color = clamp(color, vec3(0.0), vec3(maxVal));

        color *= uExposure;

        // Black level (à la GIMP Levels): lift the floor so faint sky-glow and
        // the soft bilinear halos around stars crush to true black — which also
        // makes stars read as small crisp points instead of fat blobs.
        color = max(color - uBlackLevel, vec3(0.0));

        color = ACESFilm(color);
        color = pow(color, vec3(1.0 / 2.2));           // → sRGB, matching the PBR pass
    }

    FragColor = vec4(color, 1.0);
}