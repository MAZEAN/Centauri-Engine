#version 330 core

// SSR resolve / indirect-specular compositing.
//
// The lit pass (shaderPBR.frag) already deposits an IBL specular term into the scene
// (prefiltered env * specular-BRDF). Screen-space reflections should REPLACE that term where
// they are confident, not stack on top of it — otherwise confident pixels double-count their
// reflection. So instead of adding the raw reflection, this pass blends:
//
//     reflection = mix(iblSpecular, ssrSpecular, confidence)
//
// and, since the scene already contains iblSpecular, outputs only the delta that the post
// stack adds back:  (ssrSpecular - iblSpecular) * confidence.
//
// At confidence 0 the delta is zero (the scene keeps the lit pass's environment reflection —
// no black smear where SSR misses); at confidence 1 it fully swaps in the screen reflection.
// Both terms use the SAME specular-BRDF weight so the swap is energy-consistent. This is also
// the seam a reflection probe slots into later: swap uPrefilterMap for the probe's cubemap.

in  vec2 vUv;
out vec4 FragColor;

uniform sampler2D   uSsr;       // blurred reflection: rgb = reflected radiance, a = confidence
uniform sampler2D   uDepth;     // prepass depth
uniform sampler2D   uNormal;    // prepass view-space normal, encoded to [0,1]
uniform sampler2D   uMaterial;  // r = roughness, g = metallic
uniform samplerCube uPrefilterMap;  // world-space prefiltered environment (IBL fallback)
uniform sampler2D   uBrdfLUT;   // split-sum BRDF lookup

uniform mat4  uInvProjection;   // clip -> view (reconstruct view position)
uniform mat4  uInvView;         // view -> world (orient the reflection for the cubemap)
uniform float uMaxReflectionLod;
uniform float uIblIntensity;
uniform int   uHasIBL;

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

void main()
{
    vec4  ssr  = texture(uSsr, vUv);
    float conf = ssr.a;

    float depth = texture(uDepth, vUv).r;
    if (depth >= 1.0) { FragColor = vec4(0.0); return; }   // background — no surface to reflect on

    vec2  m         = texture(uMaterial, vUv).rg;
    float roughness = m.r;
    float metallic  = m.g;

    vec3 P   = viewPos(vUv);
    vec3 N   = normalize(texture(uNormal, vUv).xyz * 2.0 - 1.0);   // view space
    vec3 V   = normalize(-P);                                      // fragment -> camera (view space)
    float NoV = max(dot(N, V), 0.0);

    // shared specular-BRDF weight. No albedo in the prepass G-buffer, so metals use an
    // untinted F0 (same approximation the march already used) — a small tint error that only
    // shows under fully-confident SSR, where the bright screen reflection dominates anyway.
    vec3 F0   = mix(vec3(0.04), vec3(1.0), metallic);
    vec3 F    = FresnelSchlickRoughness(NoV, F0, roughness);
    vec2 brdf = texture(uBrdfLUT, vec2(NoV, roughness)).rg;
    vec3 W    = F * brdf.x + brdf.y;          // specular env-BRDF weight (split-sum)

    // environment fallback: prefiltered reflection in world space, weighted like the lit pass
    vec3 iblSpec = vec3(0.0);
    if (uHasIBL == 1)
    {
        vec3 Rview  = reflect(-V, N);
        vec3 Rworld = normalize(mat3(uInvView) * Rview);
        vec3 pre    = textureLod(uPrefilterMap, Rworld, roughness * uMaxReflectionLod).rgb;
        iblSpec     = pre * W * uIblIntensity;
    }

    // screen reflection, weighted by the same specular BRDF as the fallback
    vec3 ssrSpec = ssr.rgb * W;

    // delta the post stack adds onto the scene (which already holds iblSpec from the lit pass)
    vec3 delta = (ssrSpec - iblSpec) * conf;

    FragColor = vec4(delta, 1.0);
}
