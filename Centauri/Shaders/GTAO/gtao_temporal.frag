#version 330 core

in  vec2 vUv;

out vec4 FragColor;

// Temporal accumulation for GTAO. Reprojects the previous frame's resolved AO using
// depth + this-frame/previous-frame view-projection (the same reprojection math TAA's
// velocity pass uses — duplicated here rather than shared because GTAO runs earlier in the
// frame, during the prepass stage, before TAA's own velocity buffer exists for this frame).
// A small neighbourhood clamp bounds the reprojected history so disocclusion/ghosting decays
// quickly instead of leaving stale AO behind on camera motion.

const float CLAMP_GAMMA = 1.25;

uniform sampler2D uCurrent;    // this frame's blurred AO (half-res)
uniform sampler2D uHistory;    // previous frame's resolved AO (half-res)
uniform sampler2D uDepth;      // current prepass depth, full-res ([0,1])

uniform mat4  uInvViewProj;    // inverse(view*proj), current frame (jittered)
uniform mat4  uPrevViewProj;   // view*proj, previous frame (jittered)
uniform vec2  uTexel;          // 1/half-res size
uniform float uFeedback;

// ─────────────────────────────────────────────────────────────────────────────

void main()
{
    float current = texture(uCurrent, vUv).r;

    float nmin = current;
    float nmax = current;
    for (int x = -1; x <= 1; x++)
        for (int y = -1; y <= 1; y++)
        {
            float c = texture(uCurrent, vUv + vec2(x, y) * uTexel).r;
            nmin = min(nmin, c);
            nmax = max(nmax, c);
        }

    float d = texture(uDepth, vUv).r;
    vec4  world = uInvViewProj * vec4(vUv * 2.0 - 1.0, d * 2.0 - 1.0, 1.0);
    world /= world.w;

    vec4 prev   = uPrevViewProj * world;
    vec2 prevUv = (prev.xy / prev.w) * 0.5 + 0.5;
    bool onScreen = prevUv.x >= 0.0 && prevUv.x <= 1.0 && prevUv.y >= 0.0 && prevUv.y <= 1.0;

    float hist = texture(uHistory, prevUv).r;

    float boxCenter = 0.5 * (nmax + nmin);
    float boxHalf   = 0.5 * (nmax - nmin) * CLAMP_GAMMA;
    hist = clamp(hist, boxCenter - boxHalf, boxCenter + boxHalf);

    float feedback = onScreen ? uFeedback : 0.0;
    FragColor = vec4(mix(current, hist, feedback));
}
