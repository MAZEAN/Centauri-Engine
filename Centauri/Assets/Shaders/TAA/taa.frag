#version 330 core

// Temporal anti-aliasing resolve. The scene is rendered with a per-frame sub-pixel jitter;
// we blend the current frame with reprojected history so those samples accumulate into a
// supersampled, stable image. SSR is folded into "current" so its per-frame ray noise
// accumulates too.

in  vec2 vUv;
out vec4 FragColor;

const float CLAMP_GAMMA = 1.25;

uniform sampler2D uCurrent;
uniform sampler2D uHistory;
uniform sampler2D uVelocity;    

uniform vec2  uTexel;
uniform float uFeedback;

void main()
{
    vec3 current = texture(uCurrent, vUv).rgb;

    vec3 nmin = current;
    vec3 nmax = current;
    for (int x = -1; x <= 1; x++)
        for (int y = -1; y <= 1; y++)
        {
            vec3 c = texture(uCurrent, vUv + vec2(x, y) * uTexel).rgb;
            nmin = min(nmin, c);
            nmax = max(nmax, c);
        }

    vec2 vel      = texture(uVelocity, vUv).xy;
    vec2 histUv   = vUv - vel;
    bool onScreen = histUv.x >= 0.0 && histUv.x <= 1.0 && histUv.y >= 0.0 && histUv.y <= 1.0;

    vec3 hist = texture(uHistory, histUv).rgb;
    
    vec3 boxCenter = 0.5 * (nmax + nmin);
    vec3 boxHalf   = 0.5 * (nmax - nmin) * CLAMP_GAMMA;
    
    hist = clamp(hist, boxCenter - boxHalf, boxCenter + boxHalf);

    float feedback = onScreen ? uFeedback : 0.0;
    vec3  result   = mix(current, hist, feedback);

    FragColor = vec4(result, 1.0);
}