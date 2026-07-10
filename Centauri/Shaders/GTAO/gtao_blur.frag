#version 330 core

in  vec2 vUv;

out vec4 FragColor;

// ─────────────────────────────────────────────────────────────────────────────

uniform sampler2D uGtao;

// ─────────────────────────────────────────────────────────────────────────────

// 4x4 box blur — exactly the noise tile size, so it averages out the rotation pattern
void main()
{
    vec2 texel = 1.0 / vec2(textureSize(uGtao, 0));

    float sum = 0.0;
    for (int x = -2; x < 2; x++)
        for (int y = -2; y < 2; y++)
            sum += texture(uGtao, vUv + vec2(x, y) * texel).r;

    FragColor = vec4(sum / 16.0);
}