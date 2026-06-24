#version 330 core

// First bloom step: 4-tap box downsample of the resolved HDR scene into mip0, with a
// soft-knee threshold so only bright pixels contribute and the cutoff isn't a hard edge.

in  vec2 vUv;
out vec4 FragColor;

const float maxVal = 65504.0;

uniform sampler2D uSrc;
uniform vec2  uTexel;       // 1 / source size
uniform float uThreshold;   // luma where bloom starts
uniform float uKnee;        // width of the soft shoulder

vec3 sanitize(vec3 c)
{
    c = mix(c, vec3(0.0), vec3(notEqual(c, c)));   // NaN → 0
    return clamp(c, 0.0, maxVal);                  // +Inf → maxVal
}

float karisWeight(vec3 c)
{
    float luma = dot(c, vec3(0.2126, 0.7152, 0.0722));
    return 1.0 / (1.0 + luma);
}

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
    vec3 s0 = sanitize(texture(uSrc, vUv + uTexel * vec2(-1.0, -1.0)).rgb);
    vec3 s1 = sanitize(texture(uSrc, vUv + uTexel * vec2( 1.0, -1.0)).rgb);
    vec3 s2 = sanitize(texture(uSrc, vUv + uTexel * vec2(-1.0,  1.0)).rgb);
    vec3 s3 = sanitize(texture(uSrc, vUv + uTexel * vec2( 1.0,  1.0)).rgb);

    float w0 = karisWeight(s0);
    float w1 = karisWeight(s1);
    float w2 = karisWeight(s2);
    float w3 = karisWeight(s3);

    vec3 c = (s0 * w0 + s1 * w1 + s2 * w2 + s3 * w3) / (w0 + w1 + w2 + w3);

    FragColor = vec4(prefilter(c), 1.0);
}
