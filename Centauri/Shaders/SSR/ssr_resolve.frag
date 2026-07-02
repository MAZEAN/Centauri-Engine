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
        vec3  invR = 1.0 / Rworld;
        vec3  t1   = (uProbeBoxMax - worldPos) * invR;
        vec3  t2   = (uProbeBoxMin - worldPos) * invR;
        vec3  tmax = max(t1, t2);
        float dist = max(min(tmax.x, min(tmax.y, tmax.z)), 0.0);
        vec3  dir  = (worldPos + Rworld * dist) - uProbePosition;

        // MODE A: raw probe content the floor samples (sharp mip)
        FragColor = vec4(textureLod(uProbeMap, dir, 0.0).rgb, 1.0);

        // MODE B: sampling-direction elevation (comment out MODE A to use)
        // vec3 d = normalize(dir);
        // FragColor = vec4(step(abs(d.y), 0.12), max(d.y, 0.0), max(-d.y, 0.0), 1.0);
        return;
    }
    FragColor = vec4(0.0); return;

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
