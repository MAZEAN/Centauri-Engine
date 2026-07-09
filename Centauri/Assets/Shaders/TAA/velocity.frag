#version 330 core

// Per-pixel screen-space motion vectors for TAA. Reconstructs this frame's world position
// from depth, projects it through the PREVIOUS frame's view-projection, and stores the uv
// delta. Uses unjittered matrices so the TAA jitter doesn't leak into the velocity. This is
// camera-motion only (no per-object velocity) — moving objects will ghost; static geometry
// under a moving camera reprojects correctly.

in  vec2 vUv;

out vec4 FragColor;

// ─────────────────────────────────────────────────────────────────────────────

uniform sampler2D uDepth;          // current prepass depth ([0,1])

uniform mat4 uInvViewProj;         // inverse(view*proj), current frame (jittered)
uniform mat4 uPrevViewProj;        // view*proj, previous frame (jittered)

// ─────────────────────────────────────────────────────────────────────────────

void main()
{
    float d = texture(uDepth, vUv).r;

    vec4 world = uInvViewProj * vec4(vUv * 2.0 - 1.0, d * 2.0 - 1.0, 1.0);
    world /= world.w;

    vec4 prev   = uPrevViewProj * world;
    vec2 prevUv = (prev.xy / prev.w) * 0.5 + 0.5;

    FragColor = vec4(vUv - prevUv, 0.0, 0.0);
}
