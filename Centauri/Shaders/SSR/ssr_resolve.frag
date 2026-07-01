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

    vec3 Rview  = reflect(-V, N);
    vec3 Rworld = normalize(mat3(uInvView) * Rview);
    
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
        vec3 preP = textureLod(uProbeMap, Rworld, roughness * uProbeMaxReflectionLod).rgb;
        fallbackSpec      = preP * W * uProbeIntensity;
        fallbackIntensity = uProbeIntensity;
    }

    vec3 ssrSpec = ssr.rgb * W * fallbackIntensity;

    vec3 targetSpec = mix(fallbackSpec, ssrSpec, conf);
    vec3 delta      = targetSpec - skyboxSpec;

    FragColor = vec4(delta, 1.0);
}
