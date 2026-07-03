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
uniform float uSilhouetteThreshold;   // relative depth-jump ratio that flags a silhouette edge
uniform vec2  uTexel;                 // 1 / this target's resolution

uniform mat4  uInvView;
uniform int   uHasPlanar;
uniform float uPlanarHeight;

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

float silhouetteConfidence(vec2 uv, float hitViewZ)
{
    float refDepth = max(abs(hitViewZ), 1e-3);

    float maxRelDiff = 0.0;
    maxRelDiff = max(maxRelDiff, abs(viewPos(uv + vec2( uTexel.x, 0.0)).z - hitViewZ) / refDepth);
    maxRelDiff = max(maxRelDiff, abs(viewPos(uv + vec2(-uTexel.x, 0.0)).z - hitViewZ) / refDepth);
    maxRelDiff = max(maxRelDiff, abs(viewPos(uv + vec2(0.0,  uTexel.y)).z - hitViewZ) / refDepth);
    maxRelDiff = max(maxRelDiff, abs(viewPos(uv + vec2(0.0, -uTexel.y)).z - hitViewZ) / refDepth);

    return 1.0 - smoothstep(uSilhouetteThreshold * 0.5, uSilhouetteThreshold, maxRelDiff);
}


// Planar reflection owns the flat reflector (same up-facing-at-plane-height test the resolve
// uses). Where it does, planar fully replaces SSR, so the march is skipped as wasted work.
bool onPlanarReflector(vec3 P, vec3 N)
{
    if (uHasPlanar != 1) return false;

    vec3  wPos  = (uInvView * vec4(P, 1.0)).xyz;
    vec3  wN    = normalize(mat3(uInvView) * N);
    float hMask = 1.0 - smoothstep(0.15, 0.35, abs(wPos.y - uPlanarHeight));
    float fMask = smoothstep(0.7, 0.95, wN.y);
    return hMask * fMask > 0.5;
}

// March the reflection ray in screen space between the projected endpoints; on the first
// in-front→behind crossing, binary-refine and thickness-test it. Returns the hit UV.
bool marchRay(vec2 uvP, vec2 uvQ, float invWP, float invWQ, out vec2 hitUv)
{
    float stepS = 1.0 / float(uMaxSteps);

    hitUv = vec2(0.0);
    float prevS    = 0.0;
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
                return true;
            }
            return false;
        }
        prevS = s;
        prevDiff = diff;
    }

    return false;
}

// Confidence the SSR hit is trustworthy: fades at screen edges, high roughness, long distance,
// silhouettes (background bleeding past a foreground edge) and back-facing hits.
float reflectionConfidence(vec2 hitUv, vec3 P, vec3 R, float roughness)
{
    vec3  hitPos    = viewPos(hitUv);
    vec3  hitNormal = normalize(texture(uNormal, hitUv).xyz * 2.0 - 1.0);
    float backFade  = 1.0 - smoothstep(0.0, 0.25, dot(hitNormal, R));

    vec2  ef        = smoothstep(0.0, 0.15, hitUv) * (1.0 - smoothstep(0.85, 1.0, hitUv));
    float edgeFade  = ef.x * ef.y;

    float roughFade = 1.0 - smoothstep(uRoughnessCutoff * 0.5, uRoughnessCutoff, roughness);
    float distFade  = 1.0 - clamp(length(hitPos - P) / uMaxDistance, 0.0, 1.0);
    float silFade   = silhouetteConfidence(hitUv, hitPos.z);

    return edgeFade * roughFade * distFade * silFade * backFade;
}

void main()
{
    float depth = texture(uDepth, vUv).r;
    if (depth >= 1.0) { FragColor = vec4(0.0); return; }   // background — nothing to reflect

    float roughness = texture(uMaterial, vUv).r;
    if (roughness > uRoughnessCutoff) { FragColor = vec4(0.0); return; }

    vec3 P = viewPos(vUv);                                  // view-space position
    vec3 N = normalize(texture(uNormal, vUv).xyz * 2.0 - 1.0);

    if (onPlanarReflector(P, N)) { FragColor = vec4(0.0); return; }

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

    vec2 hitUv;
    if (!marchRay(uvP, uvQ, 1.0 / clipP.w, 1.0 / clipQ.w, hitUv)) { FragColor = vec4(0.0); return; }

    vec3  reflColor  = texture(uScene, hitUv).rgb * uIntensity;
    float confidence = reflectionConfidence(hitUv, P, R, roughness);

    FragColor = vec4(reflColor, confidence);   // rgb = reflected radiance, a = confidence
}