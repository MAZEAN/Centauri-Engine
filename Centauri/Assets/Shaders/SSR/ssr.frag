#version 330 core

// Screen-space reflections. Everything happens in view space: reconstruct the fragment
// position from the prepass depth, reflect the view ray about the view-space normal, then
// linearly march that ray, projecting each step back to screen to compare against stored
// depth. On a hit, binary-refine and sample the resolved HDR scene. The result is weighted
// by Fresnel + a roughness/edge/distance fade and output as an additive reflection term.

in  vec2 vUv;
out vec4 FragColor;

uniform sampler2D uScene;     // resolved HDR scene color (what we reflect)
uniform sampler2D uDepth;     // prepass depth ([0,1], engine convention)
uniform sampler2D uNormal;    // prepass view-space normal, encoded to [0,1]
uniform sampler2D uMaterial;  // r = roughness, g = metallic

uniform mat4  uProjection;
uniform mat4  uInvProjection;

uniform float uMaxDistance;
uniform int   uMaxSteps;
uniform int   uRefineSteps;
uniform float uThickness;
uniform float uIntensity;
uniform float uRoughnessCutoff;

// reconstruct view-space position from stored depth (matches ssao.frag / CascadeBuilder)
vec3 viewPos(vec2 uv)
{
    float d   = texture(uDepth, uv).r;
    vec4  ndc = vec4(uv * 2.0 - 1.0, d * 2.0 - 1.0, 1.0);
    vec4  v   = uInvProjection * ndc;
    return v.xyz / v.w;
}

// project a view-space point to screen uv
vec2 toUv(vec3 viewP)
{
    vec4 clip = uProjection * vec4(viewP, 1.0);
    return (clip.xy / clip.w) * 0.5 + 0.5;
}

void main()
{
    float depth = texture(uDepth, vUv).r;
    if (depth >= 1.0) { FragColor = vec4(0.0); return; }   // background — nothing to reflect

    vec2  mat       = texture(uMaterial, vUv).rg;
    float roughness = mat.r;
    float metallic  = mat.g;

    if (roughness > uRoughnessCutoff) { FragColor = vec4(0.0); return; }

    vec3 P = viewPos(vUv);                                  // view-space position
    vec3 N = normalize(texture(uNormal, vUv).xyz * 2.0 - 1.0);
    vec3 V = normalize(P);                                  // camera→fragment (cam at origin)
    vec3 R = normalize(reflect(V, N));                      // reflection direction

    // ── linear march ──
    float stepLen = uMaxDistance / float(uMaxSteps);
    vec3  rayPos  = P;
    vec3  prevPos = P;
    bool  hit     = false;
    vec2  hitUv   = vec2(0.0);

    for (int i = 0; i < uMaxSteps; i++)
    {
        rayPos += R * stepLen;

        vec4 clip = uProjection * vec4(rayPos, 1.0);
        if (clip.w <= 0.0) break;
        vec2 uv = (clip.xy / clip.w) * 0.5 + 0.5;
        if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0) break;

        // view z is negative; geometry "in front of" the ray has a larger (less negative) z
        float sceneZ = viewPos(uv).z;
        float diff   = sceneZ - rayPos.z;

        if (diff > 0.0 && diff < uThickness)
        {
            // ── binary refine between the last two samples ──
            vec3 lo = prevPos, hi = rayPos;
            for (int j = 0; j < uRefineSteps; j++)
            {
                vec3 mid  = (lo + hi) * 0.5;
                vec2 muv  = toUv(mid);
                float sz  = viewPos(muv).z;
                if (sz - mid.z > 0.0) hi = mid; else lo = mid;
                hitUv = muv;
            }
            hit = true;
            break;
        }
        prevPos = rayPos;
    }

    if (!hit) { FragColor = vec4(0.0); return; }

    // ── confidence fades ──
    // screen edges: reflected detail leaving the frame has no data, so feather it out
    vec2  ef        = smoothstep(0.0, 0.15, hitUv) * (1.0 - smoothstep(0.85, 1.0, hitUv));
    float edgeFade  = ef.x * ef.y;
    // roughness: ramp off toward the cutoff
    float roughFade = 1.0 - smoothstep(uRoughnessCutoff * 0.5, uRoughnessCutoff, roughness);
    // distance: fade the far end of the ray where steps are coarsest
    float distFade  = 1.0 - clamp(length(viewPos(hitUv) - P) / uMaxDistance, 0.0, 1.0);

    // ── Fresnel weight (metal reflects strongly, dielectric only at grazing) ──
    vec3  F0       = mix(vec3(0.04), vec3(1.0), metallic);
    float cosTheta = max(dot(N, -V), 0.0);
    vec3  F        = F0 + (1.0 - F0) * pow(1.0 - cosTheta, 5.0);

    vec3 reflColor = texture(uScene, hitUv).rgb;
    vec3 result    = reflColor * F * uIntensity * edgeFade * roughFade * distFade;

    FragColor = vec4(result, 1.0);
}
