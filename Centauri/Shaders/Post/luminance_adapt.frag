#version 330 core

// Eye adaptation: blends this frame's freshly-measured 1x1 log-luminance toward a persistent
// "adapted" value, exponentially over real time (framerate-independent) rather than snapping
// straight to whatever the scene reads this instant — otherwise every quick flash or dark
// doorway would pump the exposure immediately instead of settling in over a fraction of a
// second, the way an eye (or a camera's AE) actually does.

in  vec2 vUv;

out vec4 FragColor;

// ─────────────────────────────────────────────────────────────────────────────

uniform sampler2D uCurrent;    // hardware-mipmapped log-luminance texture; uCurrentLod is its coarsest level
uniform sampler2D uPrevious;   // last frame's adapted 1x1 log-luminance
uniform float uCurrentLod;     // mip level that reduces uCurrent to ~1x1 (see AutoExposurePass)
uniform float uDeltaTime;
uniform float uAdaptSpeed;     // higher = adapts faster

// ─────────────────────────────────────────────────────────────────────────────

void main()
{
    float target = textureLod(uCurrent, vec2(0.5), uCurrentLod).r;
    float prev   = texture(uPrevious, vec2(0.5)).r;

    // Second line of defense (see luminance_prefilter.frag): this value ping-pongs back into
    // "prev" next frame regardless, so a single bad reading that slips through unguarded would
    // otherwise poison every frame after it forever — mix(NaN, x, t) is NaN for any t. Hold at
    // whichever side is still finite, so a one-off glitch self-heals on the very next good frame
    // instead of latching a permanently-blown-out exposure for the rest of the session.
    if (isnan(target) || isinf(target)) target = prev;
    if (isnan(prev)   || isinf(prev))   prev   = target;

    float t = clamp(1.0 - exp(-uAdaptSpeed * uDeltaTime), 0.0, 1.0);
    FragColor = vec4(mix(prev, target, t), 0.0, 0.0, 1.0);
}
