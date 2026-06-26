#version 330 core

in vec2 fUv;
in vec3 fNormal;
in vec3 fFragPos;
in mat3 fTBN;
in  float fViewDepth;
in  vec4 fClipPos;

out vec4 FragColor;

//  ─── constants ───────────────────────────────────────────────────────────────
const float PI               = 3.14159265359;
const int   MAX_POINT_LIGHTS = 16;
const int   MAX_SPOT_LIGHTS  = 16;
const int   MAX_CASCADES = 4;

// ─── light structs (std140 — every member padded to vec4) ───────────────────────
struct DirLight {
    vec4 direction; // xyz
    vec4 color;     // xyz
    vec4 params;    // x = intensity
};

struct PointLight {
    vec4 position;  // xyz
    vec4 color;     // xyz
    vec4 params;    // x = intensity, y = constant, z = linear, w = quadratic
};

struct SpotLight {
    vec4 position;  // xyz
    vec4 direction; // xyz
    vec4 color;     // xyz
    vec4 params;    // x = intensity, y = constant, z = linear, w = quadratic
    vec4 cutoffs;   // x = innerCos, y = outerCos
};

// ─── uniforms ─────────────────────────────────────────────────────────────────
uniform vec3 uCameraPos;

// Materials
uniform sampler2D uAlbedoMap;    // slot 0 — base color
uniform sampler2D uNormalMap;    // slot 1 — surface detail
uniform sampler2D uRoughnessMap; // slot 2 — how rough/smooth
uniform sampler2D uMetallicMap;  // slot 3 — metal or not
uniform sampler2D uAOMap;        // slot 4 — shadow in crevices

uniform int uHasAlbedo;          // 1 if bound, 0 if using scalar fallback
uniform int uHasNormal;
uniform int uHasRoughness;
uniform int uHasMetallic;

uniform float uRoughnessValue;
uniform float uMetallicValue;
uniform vec4  uColor;

// IBL
uniform samplerCube uIrradianceMap;   // unit 5
uniform samplerCube uPrefilterMap;    // unit 6
uniform sampler2D   uBrdfLUT;         // unit 7
uniform int   uHasIBL;
uniform float uMaxReflectionLod;
uniform float uIblIntensity;

// SSAO
uniform sampler2D uSsaoMap;   // unit 9
uniform int       uHasSSAO;

// Shadows
uniform sampler2DArrayShadow uShadowMap;         // unit 8 (now an array)
uniform mat4  uLightMatrices[MAX_CASCADES];
uniform float uCascadeSplits[MAX_CASCADES]; // view-space far depth per cascade
uniform float uTexelWorld[MAX_CASCADES];    // world-space size of one shadow texel, per cascade

uniform int   uCascadeCount;
uniform int   uHasShadow;
uniform float uShadowBias;
uniform float uNormalBias;
uniform int   uPcfRadius;

uniform int uShowCascades;

int SelectCascade(float viewDepth) {
    for (int i = 0; i < uCascadeCount; ++i)
        if (viewDepth < uCascadeSplits[i]) 
            return i;
    
    return uCascadeCount - 1;
}

float ShadowFactor(vec3 N, vec3 L)
{
    int c = SelectCascade(fViewDepth);

    // offset along the normal by N texels of THIS cascade — self-tunes near vs far,
    // so one bias value works across every cascade instead of being a single guess
    float nOffset = uNormalBias * uTexelWorld[c];
    vec4 ls = uLightMatrices[c] * vec4(fFragPos + N * nOffset, 1.0);
    vec3 proj = ls.xyz / ls.w * 0.5 + 0.5;
    
    if (proj.z > 1.0) 
        return 0.0;                       // beyond far plane: lit

    float bias    = max(uShadowBias * (1.0 - dot(N, L)), uShadowBias * 0.1);
    float current = proj.z - bias;

    float lit   = 0.0;
    vec2  texel = 1.0 / vec2(textureSize(uShadowMap, 0).xy);
    
    for (int x = -uPcfRadius; x <= uPcfRadius; ++x)
        for (int y = -uPcfRadius; y <= uPcfRadius; ++y)
            // vec4(uv.xy, layer, compareDepth) — GPU compares + 2x2 blends in one tap
            lit += texture(uShadowMap, vec4(proj.xy + vec2(x, y) * texel, float(c), current));

    float samples = float((2 * uPcfRadius + 1) * (2 * uPcfRadius + 1));
    return 1.0 - lit / samples;                          // shadow amount (0 = lit, 1 = shadowed)
}

// ─── lighting ──────────────────────────────────────────────────────────────────
// shared lights UBO (binding 0) — uploaded once per frame for all lit shaders
layout(std140) uniform Lights {
    DirLight   uDir;
    PointLight uPoints[MAX_POINT_LIGHTS];
    SpotLight  uSpots[MAX_SPOT_LIGHTS];
    ivec4      uCounts; // x = pointCount, y = spotCount, z = hasDir
};

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

vec3 FresnelSchlickRoughness(float cosTheta, vec3 F0, float roughness) {
    return F0 + (max(vec3(1.0 - roughness), F0) - F0) * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
}

// Geometric specular antialiasing (Kaplanyan / Frostbite). A near-mirror highlight is
// sub-pixel sharp, so it flickers as the camera moves. Widen roughness to cover how much
// the shading normal varies across this pixel, filtering the highlight instead of aliasing.
float SpecularAARoughness(float roughness, vec3 N)
{
    const float SIGMA2 = 0.25;   // screen-space variance of the pixel footprint
    const float KAPPA  = 0.18;   // clamp so the added roughness stays bounded

    vec3  dndu     = dFdx(N);
    vec3  dndv     = dFdy(N);
    float variance = SIGMA2 * (dot(dndu, dndu) + dot(dndv, dndv));

    float a2       = roughness * roughness * roughness * roughness;   // alpha²
    float filtered = clamp(a2 + min(2.0 * variance, KAPPA), 0.0, 1.0);
    return sqrt(sqrt(filtered));                                      // back to roughness
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
    vec3  lightDir    = light.position.xyz - fFragPos;
    float dist        = length(lightDir);
    vec3  L           = normalize(lightDir);

    float attenuation = 1.0 / (light.params.y
    + light.params.z * dist
    + light.params.w * dist * dist);

    float theta     = dot(L, normalize(-light.direction.xyz));
    float epsilon   = light.cutoffs.x - light.cutoffs.y;

    float coneIntensity = clamp((theta - light.cutoffs.y) / epsilon, 0.0, 1.0);
    vec3  radiance      = light.color.xyz * light.params.x * attenuation * coneIntensity;

    return CalcPBR(L, radiance, N, V, albedo, roughness, metallic);
}

void showShadowCascadesView(vec3 color, vec4  albedoSample) {
    int ci = SelectCascade(fViewDepth);
    vec3 tint = ci == 0 ? vec3(1.0, 0.3, 0.3)
            : ci == 1 ? vec3(0.3, 1.0, 0.3)
            : ci == 2 ? vec3(0.3, 0.3, 1.0)
            :           vec3(1.0, 1.0, 0.3);
    
    FragColor = vec4(color * tint, albedoSample.a);
}

// ─── main ─────────────────────────────────────────────────────────────────────
void main()
{
    vec4  albedoSample = uHasAlbedo    == 1 ? texture(uAlbedoMap,    fUv) : uColor;
    float roughness    = uHasRoughness == 1 ? texture(uRoughnessMap, fUv).r : uRoughnessValue;
    float metallic     = uHasMetallic  == 1 ? texture(uMetallicMap,  fUv).r : uMetallicValue;
    float ao           = texture(uAOMap, fUv).r;

    vec3 albedo = pow(albedoSample.rgb, vec3(2.2));
    if (albedoSample.a < 0.5) discard;

    vec3 T = fTBN[0];
    vec3 N = (uHasNormal == 1 && dot(T, T) > 1e-5)
        ? normalize(fTBN * (texture(uNormalMap, fUv).rgb * 2.0 - 1.0))
        : normalize(fNormal);

    if (!gl_FrontFacing) 
        N = -N;

    roughness = SpecularAARoughness(roughness, N);

    vec3 V  = normalize(uCameraPos - fFragPos);
    vec3 Lo = vec3(0.0);

    // directional
    if (uCounts.z == 1)
    {
        vec3 L        = normalize(-uDir.direction.xyz);
        vec3 radiance = uDir.color.xyz * uDir.params.x;
        float shadow  = uHasShadow == 1 ? ShadowFactor(N, L) : 0.0;
        Lo += CalcPBR(L, radiance, N, V, albedo, roughness, metallic) * (1.0 - shadow);
    }

    // point lights
    for (int i = 0; i < uCounts.x; i++)
    {
        vec3  lightDir    = uPoints[i].position.xyz - fFragPos;
        float dist        = length(lightDir);
        float attenuation = 1.0 / (uPoints[i].params.y
        + uPoints[i].params.z * dist
        + uPoints[i].params.w * dist * dist);

        vec3 Lp        = normalize(lightDir);
        vec3 radianceP = uPoints[i].color.xyz * uPoints[i].params.x * attenuation;
        
        Lo += CalcPBR(Lp, radianceP, N, V, albedo, roughness, metallic);
    }

    // spotlights
    for (int i = 0; i < uCounts.y; i++)
    Lo += CalcSpotLight(uSpots[i], N, V, albedo, roughness, metallic);
    
    // ambient lighting
    vec3 F0 = mix(vec3(0.04), albedo, metallic);
    vec3 ambient;
    
    if (uHasIBL == 1) {
        vec3 kS = FresnelSchlickRoughness(max(dot(N, V), 0.0), F0, roughness);
        vec3 kD = (1.0 - kS) * (1.0 - metallic);
        vec3 diffuse = texture(uIrradianceMap, N).rgb * albedo;

        vec3 R = reflect(-V, N);
        vec3 prefiltered = textureLod(uPrefilterMap, R, roughness * uMaxReflectionLod).rgb;
        vec2 brdf = texture(uBrdfLUT, vec2(max(dot(N, V), 0.0), roughness)).rg;
        vec3 specular = prefiltered * (kS * brdf.x + brdf.y);

        ambient = (kD * diffuse + specular) * ao * uIblIntensity;
    } else {
        ambient = vec3(0.03) * mix(albedo, F0, metallic) * ao;   // fallback
    }

    if (uHasSSAO == 1)
    {
        vec2 ssaoUv = (fClipPos.xy / fClipPos.w) * 0.5 + 0.5;
        ambient *= texture(uSsaoMap, ssaoUv).r;
    }

    vec3 color = ambient + Lo;

    if (uShowCascades == 1 && uHasShadow == 1) {
        showShadowCascadesView(color, albedoSample);
        return;
    }
    
    FragColor = vec4(color, albedoSample.a);

    //FragColor = vec4(ambient, 1.0);
}