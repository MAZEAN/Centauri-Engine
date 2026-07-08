#version 330 core

// Eye adaptation: blends this frame's freshly-measured 1x1 log-luminance toward a persistent
// "adapted" value, exponentially over real time (framerate-independent) rather than snapping
// straight to whatever the scene reads this instant — otherwise every quick flash or dark
// doorway would pump the exposure immediately instead of settling in over a fraction of a
// second, the way an eye (or a camera's AE) actually does.

in  vec2 vUv;

out vec4 FragColor;

// ─────────────────────────────────────────────────────────────────────────────

uniform sampler2D uCurrent;    // this frame's fully-downsampled 1x1 log-luminance
uniform sampler2D uPrevious;   // last frame's adapted 1x1 log-luminance
uniform float uDeltaTime;
uniform float uAdaptSpeed;     // higher = adapts faster

// ─────────────────────────────────────────────────────────────────────────────

void main()
{
    float target = texture(uCurrent, vec2(0.5)).r;
    float prev   = texture(uPrevious, vec2(0.5)).r;

    float t = clamp(1.0 - exp(-uAdaptSpeed * uDeltaTime), 0.0, 1.0);
    FragColor = vec4(mix(prev, target, t), 0.0, 0.0, 1.0);
}
