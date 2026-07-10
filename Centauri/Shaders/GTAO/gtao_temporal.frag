#version 330 core

in  vec2 vUv;

out vec4 FragColor;

// Temporal accumulation for GTAO. Reprojects the previous frame's resolved AO using
// depth + this-frame/previous-frame view-projection (the same reprojection math TAA's
// velocity pass uses — duplicated here rather than shared because GTAO runs earlier in the
// frame, during the prepass stage, before TAA's own velocity buffer exists for this frame).
//
// Deliberately does NOT do a TAA-style neighbourhood-clamp on history: that clamp is right for
// color TAA, where the current frame is a trustworthy sample and only reprojected history needs
// bounding against real ghosting. Here the current frame is the opposite — it's *intentionally*
// noisy (the per-pixel search rotates its sampling pattern every frame on purpose, to gain
// coverage over time), so a box built from its own local neighbourhood just drags history back
// to whatever the current frame's instantaneous, possibly-unstable value is, which quietly
// defeats accumulation exactly when it's needed most. History is trusted directly instead; the
// only rejection is the on-screen check below, so a disoccluded pixel falls back to the raw
// current-frame value for a frame or two rather than blending in a clamped-but-still-stale one.

uniform sampler2D uCurrent;    // this frame's blurred AO (half-res)
uniform sampler2D uHistory;    // previous frame's resolved AO (half-res)
uniform sampler2D uDepth;      // current prepass depth, full-res ([0,1])

uniform mat4  uInvViewProj;    // inverse(view*proj), current frame (jittered)
uniform mat4  uPrevViewProj;   // view*proj, previous frame (jittered)
uniform float uFeedback;

// ─────────────────────────────────────────────────────────────────────────────

void main()
{
    float current = texture(uCurrent, vUv).r;

    float d = texture(uDepth, vUv).r;
    if (d >= 1.0) { 
        FragColor = vec4(1.0);
        return; 
    }   // background — no history to reproject

    vec4 world = uInvViewProj * vec4(vUv * 2.0 - 1.0, d * 2.0 - 1.0, 1.0);
    world /= world.w;

    vec4 prev = uPrevViewProj * world;

    // reject points that reproject behind the previous camera: prev.xy/prev.w can still land
    // inside [0,1] by sheer arithmetic accident when prev.w is near/below zero, which would
    // otherwise pull in unrelated history data that then propagates through the ping-pong
    // buffer via bilinear sampling instead of decaying
    bool valid = prev.w > 1e-4;
    vec2 prevUv = valid ? (prev.xy / prev.w) * 0.5 + 0.5 : vec2(-1.0);
    bool onScreen = valid && prevUv.x >= 0.0 && prevUv.x <= 1.0 && prevUv.y >= 0.0 && prevUv.y <= 1.0;

    float hist = onScreen ? texture(uHistory, prevUv).r : current;

    float feedback = onScreen ? uFeedback : 0.0;
    FragColor = vec4(mix(current, hist, feedback));
}
