#version 330 core

in vec2 fUv;

uniform sampler2D uAlbedo;     // bound only for alpha-tested (foliage) casters
uniform int       uAlphaTest;  // 1 = discard by albedo alpha so leaves cast a cutout shadow

void main()
{
    // match the lit/prepass cutout so foliage casts leaf-shaped (dappled) shadows
    // instead of solid quad blocks
    if (uAlphaTest == 1 && texture(uAlbedo, fUv).a < 0.5)
        discard;
}