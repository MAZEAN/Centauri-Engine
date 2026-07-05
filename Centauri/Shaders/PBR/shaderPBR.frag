#version 330 core

in vec2 fUv;
in vec3 fNormal;
in vec3 fFragPos;
in mat3 fTBN;
in float fViewDepth;
in vec4 fClipPos;

out vec4 FragColor;

//  ─── constants ───────────────────────────────────────────────────────────────
const float PI               = 3.14159265359;
const int   MAX_POINT_LIGHTS = 16;
const int   MAX_SPOT_LIGHTS  = 16;
const int   MAX_CASCADES = 4;
const float CASCADE_BLEND = 0.1;   // fraction of a cascade's depth range used as the cross-fade band
const float SHADOW_FADE   = 0.1;   // fraction of the shadow distance over which shadows fade out
const int BLOCKER_TAPS = 8;   // subset of POISSON_DISK — cheaper than the full PCF tap count

const int  POISSON_COUNT = 16;
const vec2 POISSON_DISK[16] = vec2[](
        vec2(-0.94201624, -0.39906216), vec2( 0.94558609, -0.76890725),
        vec2(-0.09418410, -0.92938870), vec2( 0.34495938,  0.29387760),
        vec2(-0.91588581,  0.45771432), vec2(-0.81544232, -0.87912464),
        vec2(-0.38277543,  0.27676845), vec2( 0.97484398,  0.75648379),
        vec2( 0.44323325, -0.97511554), vec2( 0.53742981, -0.47373420),
        vec2(-0.26496911, -0.41893023), vec2( 0.79197514,  0.19090188),
        vec2(-0.24188840,  0.99706507), vec2(-0.81409955,  0.91437590),
        vec2( 0.19984126,  0.78641367), vec2( 0.14383161, -0.14100790)
);

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
uniform vec4 uClipPlane;

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

uniform float uRoughnessScalar;
uniform float uMetallicScalar;
uniform float uTranslucency;
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
uniform int uFoliage;   // 1 = two-sided foliage: add leaf transmission, skip screen-space AO

// Shadows
uniform sampler2DArray uShadowMap;   // unit 8 (now an array) — sampled manually (no HW compare) below
layout(std140) uniform Shadows {
    mat4 uLightMatrices[MAX_CASCADES];
    vec4 uCascadeSplits;
    vec4 uTexelWorld;
    vec4 uDepthRangeWorld;
};

uniform int   uCascadeCount;
uniform int   uHasShadow;
uniform float uShadowBias;
uniform float uNormalBias;
uniform int   uPcfRadius;

// PCSS — contact hardening
uniform int   uPcss;
uniform float uLightSize;      // tan(sun half-angle): world penumbra growth per unit occluder distance
uniform float uBlockerRadius;  // blocker-search disk radius, in texels
uniform float uMaxPenumbra;    // clamp on the resulting PCF radius, in texels

// Debugging
uniform int uShowCascades;

int SelectCascade(float viewDepth) 
{
    for (int i = 0; i < uCascadeCount; ++i)
        if (viewDepth < uCascadeSplits[i]) 
            return i;
    
    return uCascadeCount - 1;
}

mat2 InterleavedGradientRotation()
{
    float ign = fract(52.9829189 * fract(dot(gl_FragCoord.xy, vec2(0.06711056, 0.00583715))));
    float ang = ign * 6.28318530;
    float sa  = sin(ang), ca = cos(ang);
    
    return mat2(ca, -sa, sa, ca);
}

// Average depth of samples closer to the light than `current`, searched over a
// `radiusTexels` disk. Returns -1 when nothing in the window occludes the point, so the
// caller can skip the PCF pass entirely (fully lit).
float FindBlockerDepth(int c, vec2 uv, float current, float radiusTexels, float selfBias)
{
    vec2 texel = 1.0 / vec2(textureSize(uShadowMap, 0).xy);
    mat2 rot   = InterleavedGradientRotation();

    float threshold = current - selfBias;
    float sum   = 0.0;
    int   count = 0;
    for (int i = 0; i < BLOCKER_TAPS; ++i)
    {
        vec2  offset = (rot * POISSON_DISK[i]) * radiusTexels * texel;
        float z      = texture(uShadowMap, vec3(uv + offset, float(c))).r;
        if (z < threshold)
        {
            sum += z;
            count++;
        }
    }

    return count > 0 ? sum / float(count) : -1.0;
}

float SampleCascade(int c, vec3 N, vec3 L) 
{
    float nOffset = uNormalBias * uTexelWorld[c];
    vec4 ls = uLightMatrices[c] * vec4(fFragPos + N * nOffset, 1.0);
    vec3 proj = ls.xyz / ls.w * 0.5 + 0.5;

    if (proj.z > 1.0)
        return 0.0;

    float bias    = max(uShadowBias * (1.0 - dot(N, L)), uShadowBias * 0.1);
    float current = proj.z - bias;

    float radius = float(uPcfRadius);

    if (uPcss == 1)
    {
        float selfBias   = nOffset / max(uDepthRangeWorld[c], 1e-4);
        float avgBlocker = FindBlockerDepth(c, proj.xy, current, uBlockerRadius, selfBias);
        if (avgBlocker >= 0.0)
        {
            // orthographic (directional/parallel) light: penumbra grows linearly with
            // occluder distance, no perspective divide needed — unlike point/spot-light PCSS.
            float worldPenumbra  = (current - avgBlocker) * uDepthRangeWorld[c] * uLightSize;
            float penumbraTexels = worldPenumbra / uTexelWorld[c];
            radius = clamp(penumbraTexels, radius, uMaxPenumbra);
        }
    }

    vec2 texel = 1.0 / vec2(textureSize(uShadowMap, 0).xy);
    mat2  rot  = InterleavedGradientRotation();

    float lit = 0.0;
    for (int i = 0; i < POISSON_COUNT; ++i)
    {
        vec2  offset = (rot * POISSON_DISK[i]) * radius * texel;
        float z      = texture(uShadowMap, vec3(proj.xy + offset, float(c))).r;
        lit += z < current ? 0.0 : 1.0;
    }

    return 1.0 - lit / float(POISSON_COUNT);
}

float ShadowFactor(vec3 N, vec3 L)
{
    int c = SelectCascade(fViewDepth);

    float shadow = SampleCascade(c, N, L);
    
    if (c + 1 < uCascadeCount)
    {
        float splitFar  = uCascadeSplits[c];
        float splitNear = c == 0 ? 0.0 : uCascadeSplits[c - 1];
        float band      = CASCADE_BLEND * (splitFar - splitNear);

        if (band > 0.0)
        {
            float t = clamp((splitFar - fViewDepth) / band, 0.0, 1.0);  // 1 inside, 0 at the seam
            if (t < 1.0)
                shadow = mix(SampleCascade(c + 1, N, L), shadow, t);
        }
    }
    
    float maxDist = uCascadeSplits[uCascadeCount - 1];
    float fade    = clamp((maxDist - fViewDepth) / (maxDist * SHADOW_FADE), 0.0, 1.0);

    return shadow * fade;
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

void ShowShadowCascadesView(vec3 color, vec4  albedoSample) {
    int ci = SelectCascade(fViewDepth);
    vec3 tint = ci == 0 ? vec3(1.0, 0.3, 0.3)
              : ci == 1 ? vec3(0.3, 1.0, 0.3)
              : ci == 2 ? vec3(0.3, 0.3, 1.0)
              :           vec3(1.0, 1.0, 0.3);
    
    FragColor = vec4(color * tint, albedoSample.a);
}

// world-space shading normal: normal-map detail through the TBN when present (and valid),
// else the interpolated vertex normal; flipped for back faces.
vec3 SurfaceNormal()
{
    vec3 T = fTBN[0];
    vec3 N = (uHasNormal == 1 && dot(T, T) > 1e-5)
        ? normalize(fTBN * (texture(uNormalMap, fUv).rgb * 2.0 - 1.0))
        : normalize(fNormal);

    if (!gl_FrontFacing)
        N = -N;

    return N;
}

// analytic lights: directional (with CSM shadow + optional leaf translucency), point, spot.
vec3 DirectLighting(vec3 N, vec3 V, vec3 albedo, float roughness, float metallic)
{
    vec3 Lo = vec3(0.0);

    // directional
    if (uCounts.z == 1)
    {
        vec3 L        = normalize(-uDir.direction.xyz);
        vec3 radiance = uDir.color.xyz * uDir.params.x;
        float shadow  = uHasShadow == 1 ? ShadowFactor(N, L) : 0.0;

        Lo += CalcPBR(L, radiance, N, V, albedo, roughness, metallic) * (1.0 - shadow);

        if (uTranslucency > 0.0)
        {
            const float DISTORTION = 0.25;
            const float POWER      = 4.0;

            vec3  transDir = normalize(L + N * DISTORTION);
            float t        = pow(clamp(dot(V, -transDir), 0.0, 1.0), POWER);
            Lo += albedo * radiance * t * uTranslucency * (1.0 - shadow * 0.5);
        }
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

    return Lo;
}

// image-based ambient (split-sum IBL) or a flat fallback, attenuated by AO / SSAO.
vec3 AmbientLighting(vec3 N, vec3 V, vec3 albedo, float roughness, float metallic, float ao)
{
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

    if (uHasSSAO == 1 && uFoliage == 0)
    {
        vec2 ssaoUv = (fClipPos.xy / fClipPos.w) * 0.5 + 0.5;
        ambient *= texture(uSsaoMap, ssaoUv).r;
    }

    return ambient;
}

// ─── main ─────────────────────────────────────────────────────────────────────
void main()
{
    if (dot(vec4(fFragPos, 1.0), uClipPlane) < 0.0) discard;

    vec4  albedoSample = uHasAlbedo    == 1 ? texture(uAlbedoMap,    fUv) : uColor;
    float roughness    = uHasRoughness == 1 ? texture(uRoughnessMap, fUv).r : uRoughnessScalar;
    float metallic     = uHasMetallic  == 1 ? texture(uMetallicMap,  fUv).r : uMetallicScalar;
    float ao           = texture(uAOMap, fUv).r;

    vec3 albedo = pow(albedoSample.rgb, vec3(2.2));
    if (albedoSample.a < 0.5)
        discard;

    vec3 N    = SurfaceNormal();
    roughness = SpecularAARoughness(roughness, N);
    vec3 V    = normalize(uCameraPos - fFragPos);

    vec3 Lo      = DirectLighting(N, V, albedo, roughness, metallic);
    vec3 ambient = AmbientLighting(N, V, albedo, roughness, metallic, ao);

    vec3 color = ambient + Lo;

    if (uShowCascades == 1 && uHasShadow == 1) {
        ShowShadowCascadesView(color, albedoSample);
        return;
    }

    FragColor = vec4(color, albedoSample.a);
}