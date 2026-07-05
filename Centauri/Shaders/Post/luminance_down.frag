#version 330 core

// Continues the auto-exposure luminance pyramid: plain 4-tap box downsample of a single-channel
// (already-logged) luminance texture. Chained mip-to-mip down to 1x1 by AutoExposurePass.

in  vec2 vUv;
out vec4 FragColor;

uniform sampler2D uSrc;
uniform vec2  uTexel;   // 1 / source size

void main()
{
    float s0 = texture(uSrc, vUv + uTexel * vec2(-1.0, -1.0)).r;
    float s1 = texture(uSrc, vUv + uTexel * vec2( 1.0, -1.0)).r;
    float s2 = texture(uSrc, vUv + uTexel * vec2(-1.0,  1.0)).r;
    float s3 = texture(uSrc, vUv + uTexel * vec2( 1.0,  1.0)).r;

    FragColor = vec4((s0 + s1 + s2 + s3) * 0.25, 0.0, 0.0, 1.0);
}
