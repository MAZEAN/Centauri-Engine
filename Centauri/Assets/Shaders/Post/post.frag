#version 330 core

in  vec2 vUv;

out vec4 FragColor;

const float maxVal = 65504.0;

uniform sampler2D uHdr;
uniform float uExposure;     // pre-tonemap linear multiplier
uniform float uBlackLevel;   // pre-tonemap floor lifted to black
uniform float uContrast;     // post-tonemap, around mid-grey
uniform float uSaturation;   // post-tonemap

uniform sampler2D uBloom;          // accumulated bloom pyramid (mip0)
uniform int       uHasBloom;
uniform float     uBloomIntensity; // additive strength

vec3 ACESFilm(vec3 x)
{
    const float a = 2.51, b = 0.03, c = 2.43, d = 0.59, e = 0.14;
    return clamp((x * (a * x + b)) / (x * (c * x + d) + e), 0.0, 1.0);
}

void main()
{
    vec3 color = texture(uHdr, vUv).rgb;
    
    // ── add bloom in linear HDR space, before tonemapping ──
    if (uHasBloom == 1)
        color += texture(uBloom, vUv).rgb * uBloomIntensity;
    
    color = clamp(color, 0.0, maxVal);              // kill +Inf before tonemap

    // ── linear-space grading ──
    color *= uExposure;
    color = max(color - uBlackLevel, 0.0);

    // ── tonemap ──
    color = ACESFilm(color);

    // ── display-space grading ──
    color = mix(vec3(0.5), color, uContrast);                       // contrast
    
    float luma = dot(color, vec3(0.2126, 0.7152, 0.0722));
    color = mix(vec3(luma), color, uSaturation);                    // saturation
    color = clamp(color, 0.0, 1.0);

    color = pow(color, vec3(1.0 / 2.2));             // → sRGB
    
    FragColor = vec4(color, 1.0);
}