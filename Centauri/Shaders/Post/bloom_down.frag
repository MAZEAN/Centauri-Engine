#version 330 core

// 13-tap downsample (Jimenez / CoD "Next Generation Post Processing"). Overlapping taps
// give a wide, stable, firefly-resistant blur as the pyramid shrinks each level by half.

in  vec2 vUv;
out vec4 FragColor;

uniform sampler2D uSrc;
uniform vec2 uTexel;   // 1 / source size

void main()
{
    vec2 t = uTexel;

    vec3 a = texture(uSrc, vUv + t * vec2(-2.0,  2.0)).rgb;
    vec3 b = texture(uSrc, vUv + t * vec2( 0.0,  2.0)).rgb;
    vec3 c = texture(uSrc, vUv + t * vec2( 2.0,  2.0)).rgb;

    vec3 d = texture(uSrc, vUv + t * vec2(-2.0,  0.0)).rgb;
    vec3 e = texture(uSrc, vUv + t * vec2( 0.0,  0.0)).rgb;
    vec3 f = texture(uSrc, vUv + t * vec2( 2.0,  0.0)).rgb;

    vec3 g = texture(uSrc, vUv + t * vec2(-2.0, -2.0)).rgb;
    vec3 h = texture(uSrc, vUv + t * vec2( 0.0, -2.0)).rgb;
    vec3 i = texture(uSrc, vUv + t * vec2( 2.0, -2.0)).rgb;

    vec3 j = texture(uSrc, vUv + t * vec2(-1.0,  1.0)).rgb;
    vec3 k = texture(uSrc, vUv + t * vec2( 1.0,  1.0)).rgb;
    vec3 l = texture(uSrc, vUv + t * vec2(-1.0, -1.0)).rgb;
    vec3 m = texture(uSrc, vUv + t * vec2( 1.0, -1.0)).rgb;

    vec3 col = e * 0.125;
    col += (a + c + g + i) * 0.03125;
    col += (b + d + f + h) * 0.0625;
    col += (j + k + l + m) * 0.125;

    FragColor = vec4(col, 1.0);
}
