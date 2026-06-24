#version 330 core

// 9-tap tent upsample. Output is additively blended (GL blend One,One) onto the next
// larger mip, so each level's blur accumulates into a smooth, wide bloom.

in  vec2 vUv;
out vec4 FragColor;

uniform sampler2D uSrc;
uniform vec2  uTexel;    // 1 / source (smaller mip) size
uniform float uRadius;   // spread multiplier

void main()
{
    vec2 t = uTexel * uRadius;

    vec3 a = texture(uSrc, vUv + t * vec2(-1.0,  1.0)).rgb;
    vec3 b = texture(uSrc, vUv + t * vec2( 0.0,  1.0)).rgb;
    vec3 c = texture(uSrc, vUv + t * vec2( 1.0,  1.0)).rgb;
    vec3 d = texture(uSrc, vUv + t * vec2(-1.0,  0.0)).rgb;
    vec3 e = texture(uSrc, vUv + t * vec2( 0.0,  0.0)).rgb;
    vec3 f = texture(uSrc, vUv + t * vec2( 1.0,  0.0)).rgb;
    vec3 g = texture(uSrc, vUv + t * vec2(-1.0, -1.0)).rgb;
    vec3 h = texture(uSrc, vUv + t * vec2( 0.0, -1.0)).rgb;
    vec3 i = texture(uSrc, vUv + t * vec2( 1.0, -1.0)).rgb;

    vec3 col = e * 4.0;
    col += (b + d + f + h) * 2.0;
    col += (a + c + g + i);
    col *= 1.0 / 16.0;

    FragColor = vec4(col, 1.0);
}
