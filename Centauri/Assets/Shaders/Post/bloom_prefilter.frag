#version 330 core

// First bloom step: 4-tap box downsample of the resolved HDR scene into mip0, with a
// soft-knee threshold so only bright pixels contribute and the cutoff isn't a hard edge.

in  vec2 vUv;
out vec4 FragColor;

uniform sampler2D uSrc;
uniform vec2  uTexel;       // 1 / source size
uniform float uThreshold;   // luma where bloom starts
uniform float uKnee;        // width of the soft shoulder

vec3 prefilter(vec3 c)
{
    float br   = max(c.r, max(c.g, c.b));
    float soft = clamp(br - uThreshold + uKnee, 0.0, 2.0 * uKnee);
    soft       = soft * soft / (4.0 * uKnee + 1e-5);
    float w    = max(soft, br - uThreshold) / max(br, 1e-5);
    return c * w;
}

void main()
{
    // 4-tap box keeps the downsample stable before thresholding
    vec3 c  = texture(uSrc, vUv + uTexel * vec2(-1.0, -1.0)).rgb;
    c      += texture(uSrc, vUv + uTexel * vec2( 1.0, -1.0)).rgb;
    c      += texture(uSrc, vUv + uTexel * vec2(-1.0,  1.0)).rgb;
    c      += texture(uSrc, vUv + uTexel * vec2( 1.0,  1.0)).rgb;
    c      *= 0.25;

    FragColor = vec4(prefilter(c), 1.0);
}
