#version 330 core

in vec2 fUv;
in vec3 fNormal;
in vec3 fFragPos;
in mat3 fTBN;
in float fViewDepth;
in vec4 fClipPos;
in vec3 fInstanceOrigin;  // this instance's world position (iModel[3].xyz) — foliage outward-normal reference

out vec4 FragColor;

// ─────────────────────────────────────────────────────────────────────────────

//  ─── constants ───────────────────────────────────────────────────────────────
const float PI               = 3.14159265359;
const int   MAX_POINT_LIGHTS = 16;
const int   MAX_SPOT_LIGHTS  = 16;
const int   MAX_CASCADES = 4;
const float CASCADE_BLEND = 0.1;   // fraction of a cascade's depth range used as the cross-fade band
const float SHADOW_FADE = 0.1;   // fraction of the shadow distance over which shadows fade out
const int   BLOCKER_TAPS = 8;   // subset of POISSON_DISK — cheaper than the full PCF tap count
const float FOLIAGE_FRESNEL_ATTEN = 0.05;

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
const float BAYER_4X4[16] = float[](
        0.0,  8.0,  2.0, 10.0,
        12.0,  4.0, 14.0,  6.0,
        3.0, 11.0,  1.0,  9.0,
        15.0,  7.0, 13.0,  5.0
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
uniform int uHasAO;

uniform float uRoughnessScalar;
uniform float uMetallicScalar;
uniform float uTranslucency;
uniform vec4  uColor;


// Triplaner projection

// World-space tri-planar projection instead of stored mesh UVs — opt-in per material, for
// organic/branching geometry (bark, rock) where a clean unwrap isn't practical. See
// TriplanarWeights()/SampleTriplanar() below.
uniform int   uTriplanar;
uniform float uTriplanarScale;   // world meters spanned by one texture tile

// Alpha-tested cutout threshold — tunable (RenderConfig.FoliageAlphaCutoff), must match
// ZPrepass's uFoliageAlphaCutoff exactly. See RenderConfig.cs for why.
uniform float uFoliageAlphaCutoff;

// IBL
uniform samplerCube uIrradianceMap;   // unit 5
uniform samplerCube uPrefilterMap;    // unit 6
uniform sampler2D   uBrdfLUT;         // unit 7
uniform int   uHasIBL;
uniform float uMaxReflectionLod;
uniform float uIblIntensity;

// GTAO
uniform sampler2D uGtaoMap;   // unit 9
uniform int uHasGtao;
uniform int uFoliage;   // 1 = two-sided foliage: add leaf transmission, skip screen-space AO

// Shadows — two resolution tiers (see ShadowMapper): cascade 0 (near) always renders at full
// config Size; every other cascade shares a lower-resolution "far" array (ShadowConfig.
// FarCascadeScale), since they cover a much larger world-space area at the same physical
// resolution already. uShadowMapFar's layer 0 is cascade 1, layer 1 is cascade 2, etc.
uniform sampler2DArrayShadow uShadowMapNear;     // unit 8  — hardware compare, free 2x2 PCF blend per tap
uniform sampler2DArray       uShadowMapNearRaw;  // unit 10 — same depth, uncompared: PCSS blocker search only
uniform sampler2DArrayShadow uShadowMapFar;      // unit 11
uniform sampler2DArray       uShadowMapFarRaw;   // unit 12
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

// Lighting
layout(std140) uniform Lights {
    DirLight   uDir;
    PointLight uPoints[MAX_POINT_LIGHTS];
    SpotLight  uSpots[MAX_SPOT_LIGHTS];
    ivec4      uCounts; // x = pointCount, y = spotCount, z = hasDir
};

// Debugging
uniform int uShowCascades;

// 1 = skip the PCSS blocker search, the cascade cross-fade second sample, and the full IBL
// split-sum in favor of the cheap fallbacks already below — used for secondary views (planar
// reflections) whose output gets blurred/composited, where the extra fidelity isn't visible.
uniform int uCheapShading;

// ─────────────────────────────────────────────────────────────────────────────

float DitherThreshold(vec2 fragCoord)
{
    ivec2 p = ivec2(fragCoord) & 3;
    return (BAYER_4X4[p.y * 4 + p.x] + 0.5) / 16.0;
}

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
// caller can skip the PCF pass entirely (fully lit). `near`/`layer` select which resolution
// tier and which layer within it — see the uShadowMapNear/Far comment above SampleCascade.
float FindBlockerDepth(bool near, int layer, vec2 uv, float current, float radiusTexels, float selfBias, mat2 rot)
{
    vec2 texel = near
        ? 1.0 / vec2(textureSize(uShadowMapNearRaw, 0).xy)
        : 1.0 / vec2(textureSize(uShadowMapFarRaw, 0).xy);

    float threshold = current - selfBias;
    float sum   = 0.0;
    int   count = 0;
    for (int i = 0; i < BLOCKER_TAPS; ++i)
    {
        vec2  offset = (rot * POISSON_DISK[i]) * radiusTexels * texel;
        float z      = near
            ? texture(uShadowMapNearRaw, vec3(uv + offset, float(layer))).r
            : texture(uShadowMapFarRaw,  vec3(uv + offset, float(layer))).r;
        if (z < threshold)
        {
            sum += z;
            count++;
        }
    }

    return count > 0 ? sum / float(count) : -1.0;
}

// `c` indexes the global per-cascade arrays (uLightMatrices/uTexelWorld/uDepthRangeWorld,
// still one entry per cascade regardless of resolution tier). Cascade 0 samples the near
// tier's single layer; every other cascade samples the far tier at layer (c - 1) — see the
// uShadowMapNear/Far comment above.
float SampleCascade(int c, vec3 N, vec3 L, bool allowPcss)
{
    bool near  = c == 0;
    int  layer = near ? 0 : c - 1;

    float nOffset = uNormalBias * uTexelWorld[c];
    vec4 ls = uLightMatrices[c] * vec4(fFragPos + N * nOffset, 1.0);
    vec3 proj = ls.xyz / ls.w * 0.5 + 0.5;

    if (proj.z > 1.0)
        return 0.0;

    float bias    = max(uShadowBias * (1.0 - dot(N, L)), uShadowBias * 0.1);
    float current = proj.z - bias;

    float radius = float(uPcfRadius);
    mat2  rot    = InterleavedGradientRotation();

    if (uPcss == 1 && uCheapShading == 0 && allowPcss)
    {
        float selfBias   = nOffset / max(uDepthRangeWorld[c], 1e-4);
        float avgBlocker = FindBlockerDepth(near, layer, proj.xy, current, uBlockerRadius, selfBias, rot);
        if (avgBlocker >= 0.0)
        {
            // orthographic (directional/parallel) light: penumbra grows linearly with
            // occluder distance, no perspective divide needed — unlike point/spot-light PCSS.
            float worldPenumbra  = (current - avgBlocker) * uDepthRangeWorld[c] * uLightSize;
            float penumbraTexels = worldPenumbra / uTexelWorld[c];
            radius = clamp(penumbraTexels, radius, uMaxPenumbra);
        }
    }

    vec2 texel = near
        ? 1.0 / vec2(textureSize(uShadowMapNear, 0).xy)
        : 1.0 / vec2(textureSize(uShadowMapFar, 0).xy);

    float lit = 0.0;
    for (int i = 0; i < POISSON_COUNT; ++i)
    {
        // vec4(uv.xy, layer, compareDepth) — GPU compares + 2x2 blends in one tap
        vec2 offset = (rot * POISSON_DISK[i]) * radius * texel;
        lit += near
            ? texture(uShadowMapNear, vec4(proj.xy + offset, float(layer), current))
            : texture(uShadowMapFar,  vec4(proj.xy + offset, float(layer), current));
    }

    return 1.0 - lit / float(POISSON_COUNT);
}

float ShadowFactor(vec3 N, vec3 L)
{
    int c = SelectCascade(fViewDepth);

    float shadow = SampleCascade(c, N, L, true);

    if (uCheapShading == 0 && c + 1 < uCascadeCount)
    {
        float splitFar  = uCascadeSplits[c];
        float splitNear = c == 0 ? 0.0 : uCascadeSplits[c - 1];
        float band      = CASCADE_BLEND * (splitFar - splitNear);

        if (band > 0.0)
        {
            float t = clamp((splitFar - fViewDepth) / band, 0.0, 1.0);  // 1 inside, 0 at the seam
            if (t < 1.0)
                shadow = mix(SampleCascade(c + 1, N, L, false), shadow, t);
        }
    }
    
    float maxDist = uCascadeSplits[uCascadeCount - 1];
    float fade    = clamp((maxDist - fViewDepth) / (maxDist * SHADOW_FADE), 0.0, 1.0);

    return shadow * fade;
}

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
    float grazing = pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
    if (uFoliage == 1)
        grazing *= FOLIAGE_FRESNEL_ATTEN;
    return F0 + (1.0 - F0) * grazing;
}

vec3 FresnelSchlickRoughness(float cosTheta, vec3 F0, float roughness) {
    float grazing = pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
    if (uFoliage == 1)
        grazing *= FOLIAGE_FRESNEL_ATTEN;
    return F0 + (max(vec3(1.0 - roughness), F0) - F0) * grazing;
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

// Projects a texture from the three world-space axis planes and blends by how much the
// (geometric, pre-normal-map) surface normal points along each axis — sharpened so the blend
// favors the dominant axis instead of a mushy 33/33/33 mix on near-diagonal surfaces. Used
// instead of fUv when uTriplanar == 1, so material appearance no longer depends on how well a
// mesh happens to be unwrapped.
vec3 TriplanarWeights(vec3 N)
{
    vec3 w = pow(abs(N), vec3(4.0));
    return w / max(w.x + w.y + w.z, 1e-5);
}

vec4 SampleTriplanar(sampler2D tex, vec3 worldPos, vec3 weights)
{
    vec3 uv = worldPos / uTriplanarScale;
    
    return texture(tex, uv.zy) * weights.x
        + texture(tex, uv.xz) * weights.y
        + texture(tex, uv.xy) * weights.z;
}

vec4 SampleMaterialMap(sampler2D tex, vec3 triWeights)
{
    return uTriplanar == 1 ? SampleTriplanar(tex, fFragPos, triWeights) : texture(tex, fUv);
}

// Tangent-space normal maps can't be blended per-axis like color (each projection has its own
// tangent space and a naive blend cancels detail out) — "whiteout blending" (Ben Golus) adds
// each projection's tangent-space XY onto the world normal's matching components and
// reconstructs Z from it instead, so the three stay consistent before blending.
vec3 SampleTriplanarNormal(sampler2D tex, vec3 worldPos, vec3 N, vec3 weights)
{
    vec3 uv = worldPos / uTriplanarScale;

    vec3 tX = texture(tex, uv.zy).rgb * 2.0 - 1.0;
    vec3 tY = texture(tex, uv.xz).rgb * 2.0 - 1.0;
    vec3 tZ = texture(tex, uv.xy).rgb * 2.0 - 1.0;

    tX = vec3(tX.xy + N.zy, N.x);
    tY = vec3(tY.xy + N.xz, N.y);
    tZ = vec3(tZ.xy + N.xy, N.z);

    return normalize(tX.zyx * weights.x + tY.xzy * weights.y + tZ.xyz * weights.z);
}

// world-space shading normal: normal-map detail through the TBN when present (and valid),
// else the interpolated vertex normal; flipped for back faces.
vec3 SurfaceNormal()
{
    vec3 N;
    
    if (uTriplanar == 1 && uHasNormal == 1)
    {
        vec3 geoN = normalize(fNormal);
        N = SampleTriplanarNormal(uNormalMap, fFragPos, geoN, TriplanarWeights(geoN));
    }
    else
    {
        vec3 T = fTBN[0];
        N = (uHasNormal == 1 && dot(T, T) > 1e-5)
            ? normalize(fTBN * (texture(uNormalMap, fUv).rgb * 2.0 - 1.0))
            : normalize(fNormal);
    }

    if (!gl_FrontFacing)
        N = -N;

    if (uFoliage == 1 && uHasNormal == 0)
    {
        vec3 outward = normalize(fFragPos - fInstanceOrigin);
        N = normalize(mix(N, outward, 0.6));
    }

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

// image-based ambient (split-sum IBL) or a flat fallback, attenuated by AO / GTAO.
vec3 AmbientLighting(vec3 N, vec3 V, vec3 albedo, float roughness, float metallic, float ao)
{
    vec3 F0 = mix(vec3(0.04), albedo, metallic);
    vec3 ambient;

    if (uHasIBL == 1 && uCheapShading == 0) {
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

    if (uHasGtao == 1 && uFoliage == 0)
    {
        vec2 gtaoUv = (fClipPos.xy / fClipPos.w) * 0.5 + 0.5;
        ambient *= texture(uGtaoMap, gtaoUv).r;
    }

    return ambient;
}

// ─── main ─────────────────────────────────────────────────────────────────────
void main()
{
    if (dot(vec4(fFragPos, 1.0), uClipPlane) < 0.0) discard;

    vec3 triWeights = uTriplanar == 1 ? TriplanarWeights(normalize(fNormal)) : vec3(0.0);

    vec4  albedoSample = uHasAlbedo    == 1 ? SampleMaterialMap(uAlbedoMap,    triWeights) : uColor;
    float roughness    = uHasRoughness == 1 ? SampleMaterialMap(uRoughnessMap, triWeights).r : uRoughnessScalar;
    float metallic     = uHasMetallic  == 1 ? SampleMaterialMap(uMetallicMap,  triWeights).r : uMetallicScalar;
    float ao           = uHasAO == 1 ? SampleMaterialMap(uAOMap, triWeights).r : 1.0;

    // uAlbedoMap is stored premultiplied (GLTexture.Decode premultiplies LDR textures at load
    // time) specifically so mipmap generation/filtering blends toward black at transparent
    // edges instead of whatever RGB the source image happened to store there — undo that here
    // before the color is used for anything. uColor (the no-texture fallback) isn't premultiplied.
    if (uHasAlbedo == 1)
        albedoSample.rgb /= max(albedoSample.a, 1e-4);

    vec3 albedo = pow(albedoSample.rgb, vec3(2.2));
    
    // Tunable (FoliageConfig.AlphaCutoff) so this can be tuned against the actual leaf
    // texture's alpha falloff instead of guessed. Must match ZPrepass's threshold exactly — see
    // uFoliageAlphaCutoff's declaration above and RenderConfig.cs.
    if (albedoSample.a < uFoliageAlphaCutoff || albedoSample.a < DitherThreshold(gl_FragCoord.xy))
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