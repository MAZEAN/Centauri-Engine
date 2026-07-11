#version 330 core

in  vec2 vUv;

out vec4 FragColor;

// ─────────────────────────────────────────────────────────────────────────────

uniform sampler2D   uSsr;       // blurred reflection: rgb = reflected radiance, a = confidence
uniform sampler2D   uDepth;     // prepass depth
uniform sampler2D   uNormal;    // prepass view-space normal, encoded to [0,1]
uniform sampler2D   uMaterial;  // r = roughness, g = metallic
uniform sampler2D   uBrdfLUT;   // split-sum BRDF lookup

uniform mat4  uInvProjection;   // clip -> view (reconstruct view position)
uniform mat4  uInvView;         // view -> world (orient the reflection for the cubemap)

uniform samplerCube uPrefilterMap;  // world-space prefiltered environment (IBL fallback)
uniform float uMaxReflectionLod;
uniform float uIblIntensity;
uniform int   uHasIBL;

uniform samplerCube uProbeMap;
uniform float uProbeMaxReflectionLod;
uniform float uProbeIntensity;
uniform int   uHasProbe;
uniform vec3  uProbePosition;    // probe capture point (cubemap sampling center)
uniform vec3  uProbeBoxMin;      // parallax box (world space)
uniform vec3  uProbeBoxMax;
uniform float uProbeBoxFalloff;

// same screen-space AO the lit pass multiplies its ambient (incl. IBL specular) by. The
// resolve must apply the SAME attenuation or it over-subtracts skyboxSpec in AO'd areas.
uniform sampler2D uGtaoMap;
uniform int       uHasGtao;

uniform sampler2D uPlanarMap;
uniform int       uHasPlanar;
uniform float     uPlanarHeight;
uniform float     uPlanarIntensity;
uniform float     uPlanarDistortion;
uniform float     uPlanarBlur;

// ─────────────────────────────────────────────────────────────────────────────

vec3 viewPos(vec2 uv)
{
    float d   = texture(uDepth, uv).r;
    vec4  ndc = vec4(uv * 2.0 - 1.0, d * 2.0 - 1.0, 1.0);
    vec4  v   = uInvProjection * ndc;
    return v.xyz / v.w;
}

// matches shaderPBR.frag so the subtracted IBL term lines up with what the lit pass added
vec3 FresnelSchlickRoughness(float cosTheta, vec3 F0, float roughness)
{
    return F0 + (max(vec3(1.0 - roughness), F0) - F0) * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
}

// Local reflection-probe fallback, box-bounded: blends the probe's prefiltered specular over
// the skybox where the fragment is inside the probe volume.
vec3 probeFallback(vec3 skyboxSpec, vec3 Rworld, vec3 worldPos, vec3 W, float roughness)
{
    if (uHasProbe != 1) return skyboxSpec;

    vec3  outsideVec  = max(uProbeBoxMin - worldPos, worldPos - uProbeBoxMax);
    float outside     = max(outsideVec.x, max(outsideVec.y, outsideVec.z));
    float probeWeight = 1.0 - smoothstep(0.0, uProbeBoxFalloff, max(outside, 0.0));
    if (probeWeight <= 0.0) return skyboxSpec;

    vec3 preP      = textureLod(uProbeMap, Rworld, roughness * uProbeMaxReflectionLod).rgb;
    vec3 probeSpec = preP * W * uProbeIntensity;

    return mix(skyboxSpec, probeSpec, probeWeight);
}

// Planar reflection override on the flat reflector: samples the mirror texture with a
// roughness-driven 9-tap tent blur and blends it in by the up-facing-at-plane-height mask.
vec3 applyPlanar(vec3 targetSpec, vec3 worldPos, vec3 N, vec3 W, float roughness)
{
    if (uHasPlanar != 1) return targetSpec;

    vec3  Nworld     = normalize(mat3(uInvView) * N);
    float heightMask = 1.0 - smoothstep(0.15, 0.35, abs(worldPos.y - uPlanarHeight));
    float faceMask   = smoothstep(0.7, 0.95, Nworld.y);
    float planarMask = heightMask * faceMask;
    if (planarMask <= 0.0) return targetSpec;

    vec2 duv = Nworld.xz * uPlanarDistortion;   // 0 for a perfectly flat plane
    vec2 uv  = vUv + duv;

    // Explicit 9-tap tent (no mipmaps: float-texture mip chains are unreliable on some GPUs).
    // Offset scales with roughness, so a smooth floor stays sharp; a rougher one softens and
    // hides half-res / grazing pixelation.
    vec2 o = (1.0 / vec2(textureSize(uPlanarMap, 0))) * (roughness * uPlanarBlur);
    vec3 planarCol =
          texture(uPlanarMap, uv).rgb                       * 0.25
        + texture(uPlanarMap, uv + vec2( o.x, 0.0)).rgb     * 0.125
        + texture(uPlanarMap, uv + vec2(-o.x, 0.0)).rgb     * 0.125
        + texture(uPlanarMap, uv + vec2(0.0,  o.y)).rgb     * 0.125
        + texture(uPlanarMap, uv + vec2(0.0, -o.y)).rgb     * 0.125
        + texture(uPlanarMap, uv + vec2( o.x,  o.y)).rgb    * 0.0625
        + texture(uPlanarMap, uv + vec2(-o.x, -o.y)).rgb    * 0.0625
        + texture(uPlanarMap, uv + vec2( o.x, -o.y)).rgb    * 0.0625
        + texture(uPlanarMap, uv + vec2(-o.x,  o.y)).rgb    * 0.0625;

    vec3 planarSpec = planarCol * W * uPlanarIntensity;
    return mix(targetSpec, planarSpec, planarMask);
}

void main()
{
    vec4  ssr  = texture(uSsr, vUv);
    float conf = ssr.a;

    float depth = texture(uDepth, vUv).r;
    if (depth >= 1.0) { FragColor = vec4(0.0); return; }   // background — no surface to reflect on

    vec3  m         = texture(uMaterial, vUv).rgb;
    float roughness = m.r;
    float metallic  = m.g;
    float materialAo = m.b;   // baked material AO map, written by GeometryPrepass — see prepass.frag

    vec3 P   = viewPos(vUv);
    vec3 N   = normalize(texture(uNormal, vUv).xyz * 2.0 - 1.0);   // view space
    vec3 V   = normalize(-P);                                      // fragment -> camera (view space)
    float NoV = max(dot(N, V), 0.0);

    vec3 F0   = mix(vec3(0.04), vec3(1.0), metallic);
    vec3 F    = FresnelSchlickRoughness(NoV, F0, roughness);
    vec2 brdf = texture(uBrdfLUT, vec2(NoV, roughness)).rg;
    vec3 W    = F * brdf.x + brdf.y;          // specular env-BRDF weight (split-sum)

    vec3 Rworld   = normalize(mat3(uInvView) * reflect(-V, N));
    vec3 worldPos = (uInvView * vec4(P, 1.0)).xyz;

    vec3 skyboxSpec = vec3(0.0);
    if (uHasIBL == 1)
        skyboxSpec = textureLod(uPrefilterMap, Rworld, roughness * uMaxReflectionLod).rgb * W * uIblIntensity;

    vec3 fallbackSpec = probeFallback(skyboxSpec, Rworld, worldPos, W, roughness);

    // Unlike skyboxSpec/probeSpec (synthetic environment maps whose brightness is an artist
    // exposure knob, hence *uIblIntensity/*uProbeIntensity above), ssr.rgb is real scene radiance
    // sampled off-screen — already physically correct, so it isn't scaled by either fallback's
    // intensity control. SSRConfig.Intensity (baked into ssr.rgb upstream, see ssr.frag) is SSR's
    // own, independent strength knob.
    vec3 ssrSpec    = ssr.rgb * W;
    vec3 targetSpec = mix(fallbackSpec, ssrSpec, conf);
    targetSpec      = applyPlanar(targetSpec, worldPos, N, W, roughness);

    // Match the lit pass's AO attenuation (screen-space GTAO *and* the material's own baked AO
    // map) so the delta reconstructs targetSpec*ao*gtao rather than under-subtracting skyboxSpec
    // in AO'd areas — the lit pass multiplies its ambient specular by both (see shaderPBR.frag's
    // AmbientLighting), so skyboxSpec here must have the same attenuation baked in for the
    // subtraction below to actually cancel what the lit pass already added.
    float gtao  = uHasGtao == 1 ? texture(uGtaoMap, vUv).r : 1.0;
    vec3  delta = (targetSpec - skyboxSpec) * materialAo * gtao;

    FragColor = vec4(delta, 1.0);
}
