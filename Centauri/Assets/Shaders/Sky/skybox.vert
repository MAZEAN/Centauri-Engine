#version 330 core

out vec3 vDir;

// ─────────────────────────────────────────────────────────────────────────────

layout (location = 0) in vec3 aPos;   // Mesh stride is 11 floats; only position is used

uniform mat4 uView;        // rotation only — translation stripped on the CPU
uniform mat4 uProjection;

// ─────────────────────────────────────────────────────────────────────────────

void main()
{
    vDir = aPos;                                  // sample direction = cube position
    vec4 pos = uProjection * uView * vec4(aPos, 1.0);
    
    gl_Position = pos.xyww;                       // force z = w → depth = 1.0 (far plane)
}