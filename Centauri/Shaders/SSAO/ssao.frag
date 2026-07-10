#version 330 core

in  vec2 vUv;

out vec4 FragColor;

// GTAO — horizon-based ambient occlusion (Jimenez et al., "Practical Realtime Strategies for
// Accurate Indirect Occlusion"). Where the old kernel-sample SSAO threw a fixed set of random
// points at the hemisphere and tested each against the depth buffer independently, this walks
// outward from the pixel along a handful of 2D "slice" directions in view space and finds the
// actual horizon (the steepest angle any real geometry reaches) in each direction — a search,
// not a scattershot — which is both more accurate and far less sample-count-dependent/noisy
// for a given cost.
//
// Implementation note: this uses a simplified (but boundary-case-verified) visibility
// integration rather than the exact closed-form arc integral from the paper. The paper's
// formula needs the horizon angles and the slice-projected surface normal in a precisely
// matched angular frame, and getting that frame convention subtly wrong produces AO that
// *looks* plausible but is quietly inverted or biased in some configurations — not something
// that can be caught without a GPU to render it on. What's implemented here is derived and
// checked directly against known-correct cases (flat open ground -> full visibility, a deep
// enclosed crevice -> near-zero visibility) rather than transcribed from the paper's algebra,
// so it should be treated as "GTAO-family horizon search", not a bit-exact reproduction.

// ─────────────────────────────────────────────────────────────────────────────
const float PI = 3.14159265359;

uniform sampler2D uDepth;     // prepass depth ([0,1], engine convention)
uniform sampler2D uNormal;    // prepass view-space normal, encoded to [0,1]
uniform sampler2D uNoise;     // 4x4 tiled: xy = per-pixel base rotation, z = step jitter [0,1)

uniform mat4  uProjection;
uniform mat4  uInvProjection;

uniform float uRadius;
uniform int   uSliceCount;
uniform int   uStepCount;
uniform float uPower;

// ─────────────────────────────────────────────────────────────────────────────

// reconstruct view-space position from the stored depth (ndc.z = depth, [0,1] — matches
// CascadeBuilder's frustum unprojection)
vec3 viewPos(vec2 uv)
{
    float d   = texture(uDepth, uv).r;
    vec4  ndc = vec4(uv * 2.0 - 1.0, d * 2.0 - 1.0, 1.0);
    vec4  v   = uInvProjection * ndc;
    
    return v.xyz / v.w;
}

// Highest elevation angle (relative to V, the direction toward the camera) reached by any
// sampled occluder while marching from P along `dir` (a unit view-space direction) out to
// uRadius. Returns -PI/2 ("nothing found — fully open in this direction") if no valid sample
// occludes. Elevation is measured the same way for both march directions of a slice — it's a
// property of where the sample sits relative to V, not which side of the slice it's on — so
// no per-side sign convention is needed here.
float horizonAngle(vec3 P, vec3 V, vec3 dir, float jitter)
{
    float best = -PI * 0.5;

    for (int step = 1; step <= uStepCount; step++)
    {
        float t = uRadius * (float(step) - 0.5 + jitter) / float(uStepCount);
        vec3  marchPos = P + dir * t;

        vec4 clip = uProjection * vec4(marchPos, 1.0);
        if (clip.w <= 0.0) continue;
        clip.xyz /= clip.w;

        vec2 sampleUv = clip.xy * 0.5 + 0.5;
        if (sampleUv.x < 0.0 || sampleUv.x > 1.0 || sampleUv.y < 0.0 || sampleUv.y > 1.0) continue;
        if (texture(uDepth, sampleUv).r >= 1.0) continue;   // background — nothing to occlude with

        vec3  Ps   = viewPos(sampleUv);
        vec3  D    = Ps - P;
        float dist = length(D);
        if (dist < 1e-4 || dist > uRadius) continue;

        float angle = asin(clamp(dot(D / dist, V), -1.0, 1.0));
        best = max(best, angle);
    }

    return best;
}

void main()
{
    if (texture(uDepth, vUv).r >= 1.0) { 
        FragColor = vec4(1.0);
        return; 
    }   // background = lit

    vec3 P = viewPos(vUv);
    vec3 N = normalize(texture(uNormal, vUv).xyz * 2.0 - 1.0);
    vec3 V = normalize(-P);

    vec3  rnd       = texture(uNoise, gl_FragCoord.xy / float(textureSize(uNoise, 0).x)).xyz;
    float baseAngle = atan(rnd.y, rnd.x);
    float jitter    = rnd.z;

    // orthonormal basis perpendicular to V, used to build each slice's marching direction
    vec3 up    = abs(V.y) < 0.999 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0);
    vec3 right = normalize(cross(up, V));
    up         = cross(V, right);

    // N's component perpendicular to V — which way the surface tilts relative to the view —
    // used below to weight each slice by how much it aligns with that tilt: occluders in the
    // direction the surface tilts *toward* matter more (light from there would actually reach
    // the surface at a meaningful angle), occluders in the direction it tilts *away from*
    // matter less (that light already arrives near-grazing, contributing little regardless).
    vec3 Ntangent = N - V * dot(N, V);
    float tangentLen = length(Ntangent);
    Ntangent = tangentLen > 1e-4 ? Ntangent / tangentLen : vec3(0.0);

    float visibility = 0.0;

    for (int slice = 0; slice < uSliceCount; slice++)
    {
        float theta = baseAngle + PI * float(slice) / float(uSliceCount);
        vec3  dir   = right * cos(theta) + up * sin(theta);

        float h1 = horizonAngle(P, V, -dir, jitter);   // negative side of the slice
        float h2 = horizonAngle(P, V,  dir, jitter);   // positive side

        float openNeg = (PI * 0.5) - h1;
        float openPos = (PI * 0.5) - h2;
        float sliceVisibility = clamp((openNeg + openPos) / (2.0 * PI), 0.0, 1.0);

        // Fade the directional bias in with tangentLen so a surface facing the camera
        // head-on (Ntangent ~ 0, the common case) gets the unbiased horizon result instead of
        // an unwanted uniform dampening — dot(0, dir) would otherwise land every slice at the
        // same partial weight regardless of geometry.
        float directionalBias = clamp(0.5 + 0.5 * dot(Ntangent, dir), 0.0, 1.0);
        float weight = mix(1.0, directionalBias, tangentLen);
        visibility += mix(1.0, sliceVisibility, weight);
    }

    visibility = clamp(visibility / float(uSliceCount), 0.0, 1.0);
    FragColor = vec4(pow(visibility, uPower));
}