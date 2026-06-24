#version 330 core

// Temporal anti-aliasing resolve. Each frame the scene is rendered with a sub-pixel jitter;
// we blend the current frame with the reprojected history so those jittered samples
// accumulate into a supersampled image. History is reprojected via motion vectors and
// constrained to the current 3x3 neighbourhood colour box (variance clipping) so it can't
// ghost across disocclusions or moving edges.

in  vec2 vUv;
out vec4 FragColor;

uniform sampler2D uCurrent;    // this frame's resolved HDR (jittered)
uniform sampler2D uHistory;    // previous TAA output
uniform sampler2D uVelocity;   // screen-space motion vectors
uniform vec2  uTexel;          // 1 / size
uniform float uFeedback;       // history weight (e.g. 0.9)

void main()
{
    vec3 current = texture(uCurrent, vUv).rgb;

    // ── neighbourhood colour box (for clamping history) ──
    vec3 nmin = current;
    vec3 nmax = current;
    for (int x = -1; x <= 1; x++)
        for (int y = -1; y <= 1; y++)
        {
            vec3 c = texture(uCurrent, vUv + vec2(x, y) * uTexel).rgb;
            nmin = min(nmin, c);
            nmax = max(nmax, c);
        }

    // ── reproject history ──
    vec2 vel     = texture(uVelocity, vUv).xy;
    vec2 histUv  = vUv - vel;
    bool onScreen = histUv.x >= 0.0 && histUv.x <= 1.0 && histUv.y >= 0.0 && histUv.y <= 1.0;

    vec3 hist = texture(uHistory, histUv).rgb;
    hist = clamp(hist, nmin, nmax);                 // variance clip — kills ghosting

    float feedback = onScreen ? uFeedback : 0.0;    // disocclusion → fall back to current
    vec3  result   = mix(current, hist, feedback);

    FragColor = vec4(result, 1.0);
}
