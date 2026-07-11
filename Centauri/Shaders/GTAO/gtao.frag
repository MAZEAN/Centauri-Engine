#version 330 core

in  vec2 vUv;

out vec4 FragColor;

// GTAO — horizon-based ambient occlusion (Jimenez et al., "Practical Realtime Strategies for
// Accurate Indirect Occlusion"). Walks outward from the pixel along a handful of 2D "slice"
// directions in view space and finds the actual horizon (the steepest angle any real geometry
// reaches) in each direction — a search, not a scattershot — which is both more accurate and far
// less sample-count-dependent/noisy than kernel-sample SSAO for a given cost.
//
// Per slice, the view-space normal is projected into the slice plane to get its signed angle `n`
// (line ~15 in the paper's algorithm listing), and the two horizon angles are combined with `n`
// through the paper's closed-form cosine-weighted arc integral — this is what actually makes it
// GTAO rather than a plain angle-count horizon AO: near-grazing occluders contribute little,
// near-normal ones contribute a lot, and a surface can never occlude itself beyond its own tangent
// plane. The exact clamp/sign convention here (which is easy to get subtly backwards — see the
// git history) is cross-checked against Intel's XeGTAO reference implementation (MIT licensed,
// https://github.com/GameTechDev/XeGTAO), itself an implementation of the same paper, and against
// two boundary cases by hand: flat unoccluded ground -> visibility ~1, a fully closed crevice ->
// visibility ~0.

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
uniform int   uFrameIndex;   // rotates the per-pixel noise each frame so temporal accumulation
// (see gtao_temporal.frag) gains angular/step coverage over time
// instead of repeating the same fixed noise tile every frame

// ─────────────────────────────────────────────────────────────────────────────

// reconstruct view-space position from an already-sampled depth (ndc.z = depth, [0,1] — matches
// CascadeBuilder's frustum unprojection). Split out from viewPos() so callers that already had
// to fetch uDepth for another reason (e.g. the background test in marchHorizonCos below) don't
// sample the same texel twice.
vec3 viewPosFromDepth(vec2 uv, float d)
{
    vec4  ndc = vec4(uv * 2.0 - 1.0, d * 2.0 - 1.0, 1.0);
    vec4  v   = uInvProjection * ndc;

    return v.xyz / v.w;
}

vec3 viewPos(vec2 uv)
{
    return viewPosFromDepth(uv, texture(uDepth, uv).r);
}

// Marches from P along `dir` (a unit view-space direction, perpendicular to V) out to uRadius,
// tracking the highest cos(angle-to-V) reached by any sampled occluder — i.e. the strongest
// horizon on this side of the slice. `lowCos` is the "nothing found" baseline: the cosine of the
// slice-plane-projected-normal's own tangent-plane edge on this side, so a march that finds
// nothing occluding reproduces exactly the open-hemisphere-to-the-normal case rather than an
// arbitrary fully-open-relative-to-V case. Samples are faded back toward that baseline as they
// approach uRadius (instead of a hard cutoff) to avoid popping as occluders cross the radius.
float marchHorizonCos(vec3 P, vec3 V, vec3 dir, float jitter, float lowCos)
{
    float horizonCos = lowCos;

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

        float sampleDepth = texture(uDepth, sampleUv).r;
        if (sampleDepth >= 1.0) continue;   // background — nothing to occlude with

        vec3  Ps   = viewPosFromDepth(sampleUv, sampleDepth);
        vec3  D    = Ps - P;
        float dist = length(D);
        if (dist < uRadius * 0.01) continue;   // too close to be a meaningful occluder, not just precision noise

        float sampleCos = dot(D / dist, V);

        // smooth fade back to the baseline over the outer quarter of the radius, rather than a
        // hard "dist > uRadius" cutoff
        float weight = clamp(1.0 - (dist - uRadius * 0.75) / (uRadius * 0.25), 0.0, 1.0);
        sampleCos = mix(lowCos, sampleCos, weight);

        horizonCos = max(horizonCos, sampleCos);
    }

    return horizonCos;
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
    // golden-angle rotation per frame: an irrational increment so the sampled slice
    // orientations/step offsets never repeat over any short cycle of frames
    float frameRot  = float(uFrameIndex) * 2.39996323;
    float baseAngle = atan(rnd.y, rnd.x) + frameRot;
    float jitter    = fract(rnd.z + float(uFrameIndex) * 0.6180339887);

    // orthonormal basis perpendicular to V, used to build each slice's marching direction
    vec3 up    = abs(V.y) < 0.999 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0);
    vec3 right = normalize(cross(up, V));
    up         = cross(V, right);

    float visibility = 0.0;

    for (int slice = 0; slice < uSliceCount; slice++)
    {
        float theta = baseAngle + PI * float(slice) / float(uSliceCount);
        vec3  dir   = right * cos(theta) + up * sin(theta);

        // project N into this slice's plane (spanned by dir and V) and find its signed angle n,
        // measured the same way as the horizon angles below: 0 along dir/-dir (grazing), +-PI/2
        // toward/away from V. Sign is positive when N leans toward +dir.
        vec3  axis     = normalize(cross(dir, V));
        vec3  projN    = N - axis * dot(N, axis);
        float projNLen = max(length(projN), 1e-5);

        float cosNorm  = clamp(dot(projN, V) / projNLen, 0.0, 1.0);
        float signNorm = sign(dot(dir, projN));
        float n        = signNorm * acos(cosNorm);

        // "nothing found" baseline on each side: the cosine at the projected normal's own
        // tangent-plane edge (n +- PI/2) — i.e. the open-hemisphere-to-the-normal case, not an
        // arbitrary fully-open-relative-to-V case
        float lowCosPos = cos(n + PI * 0.5);
        float lowCosNeg = cos(n - PI * 0.5);

        float horizonCosPos = marchHorizonCos(P, V,  dir, jitter, lowCosPos);
        float horizonCosNeg = marchHorizonCos(P, V, -dir, jitter, lowCosNeg);

        float hPos = acos(clamp(horizonCosPos, -1.0, 1.0));
        float hNeg = -acos(clamp(horizonCosNeg, -1.0, 1.0));

        // closed-form cosine-weighted visibility integral (paper eq., one arc per side)
        float iarcPos = (cosNorm + 2.0 * hPos * sin(n) - cos(2.0 * hPos - n)) * 0.25;
        float iarcNeg = (cosNorm + 2.0 * hNeg * sin(n) - cos(2.0 * hNeg - n)) * 0.25;

        // nudge the multiplier toward 1 on near-perpendicular slices (projNLen -> 0): otherwise
        // high-slope surfaces lose almost all contribution from slices that happen to catch the
        // normal edge-on, producing visible overdarkening banding across the slope
        float sliceWeight = mix(projNLen, 1.0, 0.05);
        visibility += sliceWeight * (iarcPos + iarcNeg);
    }

    visibility = clamp(visibility / float(uSliceCount), 0.0, 1.0);
    visibility = max(0.03, visibility);   // disallow total occlusion — a visible pixel should never go fully black
    FragColor = vec4(pow(visibility, uPower));
}