#version 330 core

// Screen-space reflections. Reconstruct the fragment's view-space position from the prepass
// depth, reflect the view ray about the view-space normal, then march that ray IN SCREEN
// SPACE (uniform steps in pixels, perspective-correct depth via linear 1/w interpolation).
// Screen-space marching keeps sampling density uniform across the frame — uniform view-space
// steps undersample at grazing angles and break the reflection into stretched blocks. On a
// hit, binary-refine and sample the resolved HDR scene, weighted by Fresnel + fades.

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

// view-space z (negative, in front of camera) of a point on the ray at screen fraction s,
// given the endpoints' reciprocal-w. 1/w is linear in screen space, so this is exact.
float rayViewZ(float invWStart, float invWEnd, float s)
{
    return -1.0 / mix(invWStart, invWEnd, s);   // w = -viewZ for the engine projection
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

    // clamp the ray so it never crosses in front of the camera (view z must stay negative)
    float rayLen = uMaxDistance;
    if (R.z > 0.0)
    rayLen = min(rayLen, (-0.05 - P.z) / R.z);
    if (rayLen <= 0.0) { FragColor = vec4(0.0); return; }

    vec3 Q = P + R * rayLen;                                // ray end, view space

    // project both endpoints to screen
    vec4 clipP = uProjection * vec4(P, 1.0);
    vec4 clipQ = uProjection * vec4(Q, 1.0);
    vec2 uvP   = (clipP.xy / clipP.w) * 0.5 + 0.5;
    vec2 uvQ   = (clipQ.xy / clipQ.w) * 0.5 + 0.5;
    float invWP = 1.0 / clipP.w;
    float invWQ = 1.0 / clipQ.w;

    // ── uniform screen-space march ──
    float stepS = 1.0 / float(uMaxSteps);

    bool  hit    = false;
    vec2  hitUv  = vec2(0.0);
    float prevS  = 0.0;
    float prevDiff = 0.0;

    for (int i = 1; i <= uMaxSteps; i++)
    {
        float s  = float(i) * stepS;

        vec2  uv = mix(uvP, uvQ, s);
        if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0) break;

        float rayZ   = rayViewZ(invWP, invWQ, s);           // ray depth here (perspective-correct)
        float sceneZ = viewPos(uv).z;                       // geometry depth here
        float diff   = sceneZ - rayZ;                       // >0 → ray went behind a surface

        // A sign crossing (in-front → behind) IS the intersection. Accept it and refine to
        // the exact point — do NOT gate the coarse step on thickness: at grazing/curved hits
        // the per-step depth jump exceeds thickness, which would wrongly reject the hit and
        // leave a black miss. Thickness is applied AFTER refine, where the gap is ~0.
        if (i > 1 && prevDiff < 0.0 && diff > 0.0)
        {
            // ── binary refine in screen fraction between prevS and s ──
            float lo = prevS, hi = s;
            vec2  muv = uv;
            for (int j = 0; j < uRefineSteps; j++)
            {
                float midS = (lo + hi) * 0.5;
                muv        = mix(uvP, uvQ, midS);
                float mz   = rayViewZ(invWP, invWQ, midS);

                if (viewPos(muv).z - mz > 0.0) hi = midS; else lo = midS;
                hitUv = muv;
            }

            // reject only if even the refined point is far behind the surface (ray passed
            // behind a thin object into empty space, rather than landing on it)
            float refS = (lo + hi) * 0.5;
            if (viewPos(muv).z - rayViewZ(invWP, invWQ, refS) < uThickness)
            {
                hitUv = muv;
                hit   = true;
            }
            break;
        }
        prevS = s;
        prevDiff = diff;
    }

    if (!hit) { FragColor = vec4(0.0); return; }

    // ── confidence fades ──
    // screen edges: reflected detail leaving the frame has no data, so feather it out
    vec2  ef        = smoothstep(0.0, 0.15, hitUv) * (1.0 - smoothstep(0.85, 1.0, hitUv));
    float edgeFade  = ef.x * ef.y;
    // roughness: ramp off toward the cutoff
    float roughFade = 1.0 - smoothstep(uRoughnessCutoff * 0.5, uRoughnessCutoff, roughness);
    // distance: fade the far end of the ray where data is least reliable
    float distFade  = 1.0 - clamp(length(viewPos(hitUv) - P) / uMaxDistance, 0.0, 1.0);

    // ── Fresnel weight (metal reflects strongly, dielectric only at grazing) ──
    vec3  F0       = mix(vec3(0.04), vec3(1.0), metallic);
    float cosTheta = max(dot(N, -V), 0.0);
    vec3  F        = F0 + (1.0 - F0) * pow(1.0 - cosTheta, 5.0);

    vec3 reflColor = texture(uScene, hitUv).rgb;
    vec3 result    = reflColor * F * uIntensity * edgeFade * roughFade * distFade;

    FragColor = vec4(result, 1.0);
}