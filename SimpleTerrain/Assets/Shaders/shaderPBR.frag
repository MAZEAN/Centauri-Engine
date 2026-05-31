#version 330 core

//  ─── constants ───────────────────────────────────────────────────────────────
const float PI               = 3.14159265359;
const int   MAX_POINT_LIGHTS = 16;
const int   MAX_SPOT_LIGHTS  = 16;

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

struct SpotLight {
    vec3  position;
    vec3  direction;
    vec3  color;
    float intensity;
    float constant;
    float linear;
    float quadratic;
    float innerCutoff; // pre-converted to cosine on CPU
    float outerCutoff; // pre-converted to cosine on CPU
};

// ─── uniforms ─────────────────────────────────────────────────────────────────
uniform sampler2D uAlbedoMap;    // slot 0 — base color
uniform sampler2D uNormalMap;    // slot 1 — surface detail
uniform sampler2D uRoughnessMap; // slot 2 — how rough/smooth
uniform sampler2D uMetallicMap;  // slot 3 — metal or not
uniform sampler2D uAOMap;        // slot 4 — shadow in crevices

uniform int uHasAlbedo;          // 1 if bound, 0 if using scalar fallback
uniform int   uHasNormal;
uniform int   uHasRoughness;
uniform int   uHasMetallic;
uniform int   uHasAO;

uniform float uRoughnessValue;
uniform float uMetallicValue;
uniform vec4  uColor;

// ─── lighting ──────────────────────────────────────────────────────────────────
uniform vec3       uCameraPos;
uniform DirLight   uDirLight;

uniform PointLight uPointLights[MAX_POINT_LIGHTS];
uniform int        uPointLightCount;

uniform SpotLight  uSpotLights[MAX_SPOT_LIGHTS];
uniform int        uSpotLightCount;

// ─── inputs ───────────────────────────────────────────────────────────────────
in vec2 fUv;
in vec3 fNormal;
in vec3 fFragPos;
in mat3 fTBN;

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
vec3 CalcPBR(vec3 L, vec3 radiance, vec3 N, vec3 V, vec3 albedo, float roughness, float metallic)
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

vec3 CalcSpotLight(SpotLight light, vec3 N, vec3 V, vec3 albedo, float roughness, float metallic)
{
    vec3  lightDir    = light.position - fFragPos;
    float dist        = length(lightDir);
    vec3  L           = normalize(lightDir);

    // attenuation — same as point light
    float attenuation = 1.0 / (light.constant
                      + light.linear    * dist
                      + light.quadratic * dist * dist);

    // spotlight cone — angle between light direction and fragment direction
    float theta     = dot(L, normalize(-light.direction));
    float epsilon   = light.innerCutoff - light.outerCutoff;

    // smoothstep between inner and outer cone edges
    // inside inner cone = full intensity
    // between inner/outer = soft falloff
    // outside outer cone = no light
    float coneIntensity = clamp((theta - light.outerCutoff) / epsilon, 0.0, 1.0);

    vec3 radiance = light.color * light.intensity * attenuation * coneIntensity;

    return CalcPBR(L, radiance, N, V, albedo, roughness, metallic);
}

// ─── main ─────────────────────────────────────────────────────────────────────
void main()
{
    vec4  albedoSample = uHasAlbedo    == 1 ? texture(uAlbedoMap,    fUv) : uColor;
    float roughness    = uHasRoughness == 1 ? texture(uRoughnessMap, fUv).r : uRoughnessValue;
    float metallic     = uHasMetallic  == 1 ? texture(uMetallicMap,  fUv).r : uMetallicValue;
    float ao           = uHasAO        == 1 ? texture(uAOMap,        fUv).r : 1.0;

    vec3 albedo = pow(albedoSample.rgb, vec3(2.2));
    if (albedoSample.a < 0.1) discard;

    vec3 N = uHasNormal == 1
        ? normalize(fTBN * (texture(uNormalMap, fUv).rgb * 2.0 - 1.0))
        : normalize(fNormal);

    vec3 V  = normalize(uCameraPos - fFragPos);
    vec3 Lo = vec3(0.0);

    // directional
    vec3 L        = normalize(-uDirLight.direction);
    vec3 radiance = uDirLight.color * uDirLight.intensity;
    Lo += CalcPBR(L, radiance, N, V, albedo, roughness, metallic);

    // point lights
    for (int i = 0; i < uPointLightCount; i++)
    {
        vec3  lightDir    = uPointLights[i].position - fFragPos;
        float dist        = length(lightDir);
        float attenuation = 1.0 / (uPointLights[i].constant
                          + uPointLights[i].linear    * dist
                          + uPointLights[i].quadratic * dist * dist);

        vec3 Lp        = normalize(lightDir);
        vec3 radianceP = uPointLights[i].color * uPointLights[i].intensity * attenuation;
        Lo += CalcPBR(Lp, radianceP, N, V, albedo, roughness, metallic);
    }

    // spotlights
    for (int i = 0; i < uSpotLightCount; i++)
        Lo += CalcSpotLight(uSpotLights[i], N, V, albedo, roughness, metallic);

    vec3 ambient = vec3(0.03) * albedo * ao;
    vec3 color   = ambient + Lo;

    color = color / (color + vec3(1.0));
    color = pow(color, vec3(1.0 / 2.2));

    FragColor = vec4(color, albedoSample.a);
}