#version 330 core

in vec3 vViewNormal;
in vec2 fUv;

layout (location = 0) out vec4 gNormal;   // view-space normal, encoded to [0,1]

uniform sampler2D uAlbedo;     // bound only for alpha-tested (foliage) materials
uniform int       uAlphaTest;  // 1 = discard by albedo alpha so SSAO sees the leaf cutout

void main()
{
    // match the lit pass cutout so the prepass depth/normals follow the actual leaf shape,
    // not the full quad — otherwise SSAO occludes from the transparent quad regions
    if (uAlphaTest == 1 && texture(uAlbedo, fUv).a < 0.5)
        discard;

    vec3 n = normalize(vViewNormal);
    gNormal = vec4(n * 0.5 + 0.5, 1.0);
}
