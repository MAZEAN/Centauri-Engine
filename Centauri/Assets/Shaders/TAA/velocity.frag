#version 330 core

// Per-pixel screen-space motion vectors for TAA. Reconstructs this frame's world position
// from depth, projects it through the PREVIOUS frame's view-projection, and stores the uv
// delta. Uses unjittered matrices so the TAA jitter doesn't leak into the velocity. This is
// camera-motion only (no per-object velocity) — moving objects will ghost; static geometry
// under a moving camera reprojects correctly.

in  vec2 vUv;
out vec4 FragColor;

uniform sampler2D uDepth;          // current prepass depth ([0,1])

uniform mat4 uInvProjection;       // current, unjittered — depth → view space
uniform mat4 uInvView;             // current — view → world
uniform mat4 uPrevView;            // previous frame
uniform mat4 uPrevProjection;      // previous frame, unjittered

void main()
{
    float d = texture(uDepth, vUv).r;

    // current view-space position (matches ssao.frag reconstruction)
    vec4 vpos = uInvProjection * vec4(vUv * 2.0 - 1.0, d * 2.0 - 1.0, 1.0);
    vpos /= vpos.w;

    vec4 world    = uInvView * vec4(vpos.xyz, 1.0);
    vec4 prevClip = uPrevProjection * uPrevView * world;
    vec2 prevUv   = (prevClip.xy / prevClip.w) * 0.5 + 0.5;

    FragColor = vec4(vUv - prevUv, 0.0, 0.0);
}
