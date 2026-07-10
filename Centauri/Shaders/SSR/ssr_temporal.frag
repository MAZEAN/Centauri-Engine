#version 330 core

in  vec2 vUv;

// Temporal accumulation for SSR. The per-frame reflection hit/confidence is unstable on fine or
// thin geometry (teeth, ridge spikes): whether the march lands on the detail or slips into the
// gap behind it is sensitive to sub-pixel changes, so it can flip frame to frame under camera
// motion or TAA's jitter alone — with no smoothing, that shows up directly as flicker/pixelation,
// worst at low roughness where the reflection is close to full-strength scene radiance rather
// than blurred/dimmed enough to hide the swing. This averages the (color, confidence) signal over
// time the same way gtao_temporal.frag does for AO: reproject via depth + view-projection, and
// reject history using a stored view-space Z rather than a colour-neighbourhood clamp, so
// background reflections (or lack thereof) don't bilinearly bleed across silhouette edges into
// foreground geometry the way a naive clamp allows.

// ─────────────────────────────────────────────────────────────────────────────

layout(location = 0) out vec4 oColor;   // rgb = reflected radiance, a = confidence
layout(location = 1) out vec4 oViewZ;   // r = this pixel's view-space Z (for next frame's rejection)

uniform sampler2D uCurrent;      // this frame's blurred SSR: rgb/a as above
uniform sampler2D uHistory;      // previous resolved frame's color/confidence
uniform sampler2D uHistoryZ;     // previous resolved frame's stored view-Z
uniform sampler2D uDepth;        // current prepass depth, full-res ([0,1])

uniform mat4  uInvProjection;    // view-space reconstruction (matches ssr.frag's viewPos)
uniform mat4  uInvViewProj;      // world reconstruction for reprojection (current frame)
uniform mat4  uPrevViewProj;     // view*proj, previous frame
uniform float uFeedback;

// ─────────────────────────────────────────────────────────────────────────────

float viewZ(float d)
{
    vec4 ndc = vec4(vUv * 2.0 - 1.0, d * 2.0 - 1.0, 1.0);
    vec4 v   = uInvProjection * ndc;
    
    return v.z / v.w;
}

void main()
{
    vec4 current = texture(uCurrent, vUv);

    float d = texture(uDepth, vUv).r;
    if (d >= 1.0)
    { 
        oColor = vec4(0.0);
        oViewZ = vec4(0.0);
        return; 
    }   // background

    float curZ = viewZ(d);

    vec4 world = uInvViewProj * vec4(vUv * 2.0 - 1.0, d * 2.0 - 1.0, 1.0);
    world /= world.w;

    vec4 prev = uPrevViewProj * world;

    bool valid  = prev.w > 1e-4;
    vec2 prevUv = valid ? (prev.xy / prev.w) * 0.5 + 0.5 : vec2(-1.0);
    bool onScreen = valid
        && prevUv.x >= 0.0 && prevUv.x <= 1.0
        && prevUv.y >= 0.0 && prevUv.y <= 1.0;

    vec4  hist  = onScreen ? texture(uHistory,  prevUv) : current;
    float histZ = onScreen ? texture(uHistoryZ, prevUv).r : curZ;

    bool zMatch = abs(curZ - histZ) <= 0.05 * abs(curZ);

    float feedback = (onScreen && zMatch) ? uFeedback : 0.0;

    oColor = mix(current, hist, feedback);
    oViewZ = vec4(curZ, 0.0, 0.0, 1.0);
}
