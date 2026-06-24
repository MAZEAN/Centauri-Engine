#version 330 core

in vec3 vViewNormal;
in vec2 vUv;

layout (location = 0) out vec4 gNormal;   // view-space normal, encoded to [0,1]
layout (location = 1) out vec4 gMaterial;   // r = roughness, g = metallic (for SSR)

uniform sampler2D uRoughnessMap;
uniform sampler2D uMetallicMap;
uniform int   uHasRoughness;
uniform int   uHasMetallic;
uniform float uRoughnessValue;
uniform float uMetallicValue;

void main()
{
    vec3 n = normalize(vViewNormal);
    gNormal = vec4(n * 0.5 + 0.5, 1.0);

    float roughness = uHasRoughness == 1 ? texture(uRoughnessMap, vUv).r : uRoughnessValue;
    float metallic  = uHasMetallic  == 1 ? texture(uMetallicMap,  vUv).r : uMetallicValue;
    gMaterial = vec4(roughness, metallic, 0.0, 1.0);
}
