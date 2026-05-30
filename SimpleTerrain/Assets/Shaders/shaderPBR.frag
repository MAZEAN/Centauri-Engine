#version 330 core

const float PI               = 3.14159265359;
const int   MAX_POINT_LIGHTS = 16;

// ─── structs ──────────────────────────────────────────────────────────────────
struct DirLight {
    vec3  direction;
    vec3  color;
    float intensity;
};

struct PointLight {
    vec3  position;
    vec3  color;
    float intensity;
    float constant;
    float linear;
    float quadratic;
};

// ─── uniforms ─────────────────────────────────────────────────────────────────
uniform sampler2D uAlbedoMap;
uniform sampler2D uNormalMap;
uniform sampler2D uRoughnessMap;
uniform sampler2D uMetallicMap;
uniform sampler2D uAOMap;

uniform int   uHasAlbedo;
uniform int   uHasNormal;
uniform int   uHasRoughness;
uniform int   uHasMetallic;
uniform int   uHasAO;

uniform float uRoughnessValue;
uniform float uMetallicValue;
uniform vec4  uColor;

uniform vec3       uCameraPos;
uniform DirLight   uDirLight;
uniform PointLight uPointLights[MAX_POINT_LIGHTS];
uniform int        uPointLightCount;

// ─── inputs ───────────────────────────────────────────────────────────────────
in vec2 fUv;
in vec3 fNormal;
in vec3 fFragPos;

out vec4 FragColor;

// ─── PBR functions ────────────────────────────────────────────────────────────

// normal distribution — how many microfacets align with halfway vector
// sharp highlight on smooth surfaces, spread out on rough ones
float DistributionGGX(vec3 N, vec3 H, float roughness)
{
    float a      = roughness * roughness;
    float a2     = a * a;
    float NdotH  = max(dot(N, H), 0.0);
    float NdotH2 = NdotH * NdotH;

    float denom = (NdotH2 * (a2 - 1.0) + 1.0);
    return a2 / (PI * denom * denom);
}

// geometry — self-shadowing of microfacets at grazing angles
float GeometrySchlick(float NdotV, float roughness)
{
    float r = roughness + 1.0;
    float k = (r * r) / 8.0;
    return NdotV / (NdotV * (1.0 - k) + k);
}

float GeometrySmith(vec3 N, vec3 V, vec3 L, float roughness)
{
    float NdotV = max(dot(N, V), 0.0);
    float NdotL = max(dot(N, L), 0.0);
    return GeometrySchlick(NdotV, roughness) * GeometrySchlick(NdotL, roughness);
}

// fresnel — how reflective a surface is at grazing angles
// metals reflect their color, non-metals reflect white
vec3 FresnelSchlick(float cosTheta, vec3 F0)
{
    return F0 + (1.0 - F0) * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
}

// ─── per-light PBR calculation ────────────────────────────────────────────────
vec3 CalcPBR(vec3 L, vec3 radiance, vec3 N, vec3 V,
vec3 albedo, float roughness, float metallic)
{
    // F0 = base reflectivity
    // non-metals reflect grey (0.04), metals reflect their albedo color
    vec3 F0 = mix(vec3(0.04), albedo, metallic);

    vec3  H       = normalize(V + L);
    float NdotL   = max(dot(N, L), 0.0);

    // cook-torrance BRDF
    float NDF = DistributionGGX(N, H, roughness);
    float G   = GeometrySmith(N, V, L, roughness);
    vec3  F   = FresnelSchlick(max(dot(H, V), 0.0), F0);

    // specular component
    vec3  num   = NDF * G * F;
    float denom = 4.0 * max(dot(N, V), 0.0) * NdotL + 0.0001;
    vec3  spec  = num / denom;

    // diffuse component — metals have no diffuse
    vec3 kD = (vec3(1.0) - F) * (1.0 - metallic);

    return (kD * albedo / PI + spec) * radiance * NdotL;
}

// ─── main ─────────────────────────────────────────────────────────────────────
void main()
{
    // sample maps or use scalar fallbacks
    vec4  albedoSample = uHasAlbedo    == 1 ? texture(uAlbedoMap,    fUv) : uColor;
    float roughness    = uHasRoughness == 1 ? texture(uRoughnessMap, fUv).r : uRoughnessValue;
    float metallic     = uHasMetallic  == 1 ? texture(uMetallicMap,  fUv).r : uMetallicValue;
    float ao           = uHasAO        == 1 ? texture(uAOMap,        fUv).r : 1.0;

    vec3 albedo = pow(albedoSample.rgb, vec3(2.2)); // gamma correction
    if (albedoSample.a < 0.1) discard;

    // normal — use map if available, else use interpolated vertex normal
    vec3 N = uHasNormal == 1
    ? normalize(texture(uNormalMap, fUv).rgb * 2.0 - 1.0)
    : normalize(fNormal);

    vec3 V = normalize(uCameraPos - fFragPos);

    vec3 Lo = vec3(0.0);

    // directional light
    vec3 L         = normalize(-uDirLight.direction);
    vec3 radiance  = uDirLight.color * uDirLight.intensity;
    Lo += CalcPBR(L, radiance, N, V, albedo, roughness, metallic);

    // point lights
    for (int i = 0; i < uPointLightCount; i++)
    {
        vec3  lightDir    = uPointLights[i].position - fFragPos;
        float dist        = length(lightDir);
        float attenuation = 1.0 / (uPointLights[i].constant
        + uPointLights[i].linear    * dist
        + uPointLights[i].quadratic * dist * dist);

        vec3 Lp       = normalize(lightDir);
        vec3 radianceP = uPointLights[i].color * uPointLights[i].intensity * attenuation;
        Lo += CalcPBR(Lp, radianceP, N, V, albedo, roughness, metallic);
    }

    // ambient using AO map
    vec3 ambient = vec3(0.03) * albedo * ao;
    vec3 color   = ambient + Lo;

    // HDR tone mapping + gamma correction
    color = color / (color + vec3(1.0));        // Reinhard tone map
    color = pow(color, vec3(1.0 / 2.2));        // gamma correction

    FragColor = vec4(color, albedoSample.a);
}