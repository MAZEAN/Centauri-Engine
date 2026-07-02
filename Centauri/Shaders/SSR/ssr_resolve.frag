#version 330 core

in  vec2 vUv;
out vec4 FragColor;

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
uniform sampler2D uSsaoMap;
uniform int       uHasSSAO;

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
    
    vec3 F0   = mix(vec3(0.04), vec3(1.0), metallic);
    vec3 F    = FresnelSchlickRoughness(NoV, F0, roughness);
    vec2 brdf = texture(uBrdfLUT, vec2(NoV, roughness)).rg;
    vec3 W    = F * brdf.x + brdf.y;          // specular env-BRDF weight (split-sum)

    vec3 Rview   = reflect(-V, N);
    vec3 Rworld  = normalize(mat3(uInvView) * Rview);
    vec3 worldPos = (uInvView * vec4(P, 1.0)).xyz;
    
    vec3 skyboxSpec = vec3(0.0);
    if (uHasIBL == 1)
    {
        vec3 pre  = textureLod(uPrefilterMap, Rworld, roughness * uMaxReflectionLod).rgb;
        skyboxSpec = pre * W * uIblIntensity;
    }

    vec3  fallbackSpec      = skyboxSpec;
    float fallbackIntensity = uIblIntensity;
    if (uHasProbe == 1)
    {
        // Fragment gate: the probe is only authoritative for surfaces inside its box volume,
        // fading out over uProbeBoxFalloff so its influence doesn't pop at the boundary.
        vec3  outsideVec  = max(uProbeBoxMin - worldPos, worldPos - uProbeBoxMax);
        float outside     = max(outsideVec.x, max(outsideVec.y, outsideVec.z));
        float probeWeight = 1.0 - smoothstep(0.0, uProbeBoxFalloff, max(outside, 0.0));

        if (probeWeight > 0.0)
        {
            // Sample the probe with the true reflection direction (no parallax box-projection).
            // The probe cubemap baked BOTH the sky and the scene objects from its capture point,
            // so Rworld returns the sky where it points up and the objects where it points at the
            // cluster -- exactly the occluded reflections SSR can't see. Box-projection only holds
            // when the box proxies real enclosing geometry (an indoor room); on an open scene its
            // top plane re-aims every upward reflection to a flat horizontal sample of the sky.
            vec3 preP      = textureLod(uProbeMap, Rworld, roughness * uProbeMaxReflectionLod).rgb;
            vec3 probeSpec = preP * W * uProbeIntensity;

            fallbackSpec      = mix(skyboxSpec, probeSpec,       probeWeight);
            fallbackIntensity = mix(uIblIntensity, uProbeIntensity, probeWeight);
        }
    }

    vec3 ssrSpec = ssr.rgb * W * fallbackIntensity;

    vec3 targetSpec = mix(fallbackSpec, ssrSpec, conf);

    // Match the lit pass's AO attenuation. The scene already holds skyboxSpec * ssao (the lit
    // pass multiplies its whole ambient term by ssao); scaling the delta by the same ssao makes
    //   final = skyboxSpec*ssao + (targetSpec - skyboxSpec)*ssao = targetSpec*ssao,
    // i.e. an AO-attenuated reflection. Without this the delta subtracts the FULL skyboxSpec
    // while the scene only holds the darkened one, over-subtracting to black in contact-AO
    // areas (the voids around the cube's base and its reflection).
    float ssao  = uHasSSAO == 1 ? texture(uSsaoMap, vUv).r : 1.0;
    vec3  delta = (targetSpec - skyboxSpec) * ssao;

    FragColor = vec4(delta, 1.0);
}
