#version 330 core

// First step of the auto-exposure luminance pyramid: 4-tap box downsample of the resolved HDR
// scene into log-luminance at half resolution. Logging here (rather than at the end) means the
// downsample chain averages in log space — the usual "geometric mean" luminance measure, which
// keeps a handful of very bright pixels (a window, a light) from dominating the reading the way
// a plain linear average would.

in  vec2 vUv;

out vec4 FragColor;

// ─────────────────────────────────────────────────────────────────────────────

uniform sampler2D uSrc;
uniform vec2  uTexel;   // 1 / source size

// ─────────────────────────────────────────────────────────────────────────────

void main()
{
    vec3 s0 = texture(uSrc, vUv + uTexel * vec2(-1.0, -1.0)).rgb;
    vec3 s1 = texture(uSrc, vUv + uTexel * vec2( 1.0, -1.0)).rgb;
    vec3 s2 = texture(uSrc, vUv + uTexel * vec2(-1.0,  1.0)).rgb;
    vec3 s3 = texture(uSrc, vUv + uTexel * vec2( 1.0,  1.0)).rgb;

    vec3  avg  = (s0 + s1 + s2 + s3) * 0.25;
    float luma = dot(avg, vec3(0.2126, 0.7152, 0.0722));

    FragColor = vec4(log(max(luma, 1e-4)), 0.0, 0.0, 1.0);
}
