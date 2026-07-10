#version 330 core

in  vec2 vUv;

out vec4 FragColor;

// Temporal accumulation for GTAO. Reprojects the previous frame's resolved AO using
// depth + this-frame/previous-frame view-projection (the same reprojection math TAA's
// velocity pass uses — duplicated here rather than shared because GTAO runs earlier in the
// frame, during the prepass stage, before TAA's own velocity buffer exists for this frame).
//
// Rejection is depth-based, not the TAA-style colour-neighbourhood clamp. The clamp is wrong
// here for two reasons: (1) GTAO's current frame is *intentionally* noisy (its sampling pattern
// rotates every frame on purpose to gain coverage over time), so a box built from the current
// frame's own neighbourhood just drags history back to that noisy value and defeats accumulation;
// (2) it does nothing about the real failure mode observed here — background (AO = 1, "fully
// lit") bilinearly bleeding into geometry silhouettes on reprojection and, under high feedback,
// diffusing inward frame after frame into a growing white patch. Instead we store the view-space
// Z each history pixel was written with (green channel) and reject history whose Z disagrees with
// this pixel's surface: at a silhouette the reprojected/bilinear-blended history Z (far, from the
// background) differs enormously from the geometry's near Z, so it's dropped and the pixel falls
// back to its own current-frame value instead of accumulating the bleed.

// ─────────────────────────────────────────────────────────────────────────────

uniform sampler2D uCurrent;      // this frame's blurred AO (half-res), .r
uniform sampler2D uHistory;      // previous resolved frame: .r = AO, .g = view-space Z
uniform sampler2D uDepth;        // current prepass depth, full-res ([0,1])

uniform mat4  uInvProjection;    // view-space reconstruction (matches gtao.frag's viewPos)
uniform mat4  uInvViewProj;      // world reconstruction for reprojection (current frame)
uniform mat4  uPrevViewProj;     // view*proj, previous frame
uniform float uFeedback;

// ─────────────────────────────────────────────────────────────────────────────

// view-space Z for the current pixel from its depth (linear in camera distance, so a plain
// relative threshold behaves consistently at any range — unlike raw non-linear depth)
float viewZ(float d)
{
    vec4 ndc = vec4(vUv * 2.0 - 1.0, d * 2.0 - 1.0, 1.0);
    vec4 v   = uInvProjection * ndc;

    return v.z / v.w;
}

void main()
{
    float current = texture(uCurrent, vUv).r;

    float d = texture(uDepth, vUv).r;
    if (d >= 1.0) { 
        FragColor = vec4(1.0, 0.0, 0.0, 1.0);
        return; 
    }   // background — nothing to accumulate

    float curZ = viewZ(d);

    vec4 world = uInvViewProj * vec4(vUv * 2.0 - 1.0, d * 2.0 - 1.0, 1.0);
    world /= world.w;

    vec4 prev = uPrevViewProj * world;

    // reject reprojection behind the previous camera (prev.w <= 0 can still divide into a
    // [0,1] uv by accident and pull in unrelated history)
    bool valid  = prev.w > 1e-4;
    vec2 prevUv = valid ? (prev.xy / prev.w) * 0.5 + 0.5 : vec2(-1.0);
    bool onScreen = valid
        && prevUv.x >= 0.0 && prevUv.x <= 1.0
        && prevUv.y >= 0.0 && prevUv.y <= 1.0;

    vec2  histRG = onScreen ? texture(uHistory, prevUv).rg : vec2(current, curZ);
    float histAo = histRG.r;
    float histZ  = histRG.g;

    // depth rejection: keep history only when its stored surface Z matches this pixel's. Kills
    // the silhouette bleed (background far-Z vs geometry near-Z) and disocclusion ghosting.
    bool zMatch = abs(curZ - histZ) <= 0.05 * abs(curZ);

    float feedback = (onScreen && zMatch) ? uFeedback : 0.0;
    float ao = mix(current, histAo, feedback);

    FragColor = vec4(ao, curZ, 0.0, 1.0);   // stash view-Z for next frame's rejection test
}
