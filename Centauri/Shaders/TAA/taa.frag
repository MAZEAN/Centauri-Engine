#version 330 core

in  vec2 vUv;

out vec4 FragColor;

// ─────────────────────────────────────────────────────────────────────────────

const float CLAMP_GAMMA = 1.25;

uniform sampler2D uCurrent;
uniform sampler2D uHistory;
uniform sampler2D uVelocity;
uniform sampler2D uSsr;
uniform int   uHasSsr;  

uniform vec2  uTexel;
uniform float uFeedback;

// ─────────────────────────────────────────────────────────────────────────────

vec3 sceneAt(vec2 uv)
{
    vec3 c = texture(uCurrent, uv).rgb;
    if (uHasSsr == 1) 
        c += texture(uSsr, uv).rgb;
    return c;
}

void main()
{
    vec3 current = sceneAt(vUv);

    vec3 nmin = current;
    vec3 nmax = current;
    for (int x = -1; x <= 1; x++)
        for (int y = -1; y <= 1; y++)
        {
            vec3 c = sceneAt(vUv + vec2(x, y) * uTexel);
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