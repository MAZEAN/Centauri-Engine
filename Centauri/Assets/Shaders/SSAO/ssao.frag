#version 330 core

in  vec2 vUv;
out vec4 FragColor;

uniform sampler2D uDepth;     // prepass depth ([0,1], engine convention)
uniform sampler2D uNormal;    // prepass view-space normal, encoded to [0,1]
uniform sampler2D uNoise;     // 4x4 tiled rotation vectors

uniform mat4  uProjection;
uniform mat4  uInvProjection;

uniform vec3  uKernel[64];
uniform int   uKernelSize;
uniform float uRadius;
uniform float uBias;
uniform float uPower;

// reconstruct view-space position from the stored depth (ndc.z = depth, [0,1] — matches
// CascadeBuilder's frustum unprojection)
vec3 viewPos(vec2 uv)
{
    float d   = texture(uDepth, uv).r;
    vec4  ndc = vec4(uv * 2.0 - 1.0, d * 2.0 - 1.0, 1.0);
    vec4  v   = uInvProjection * ndc;
    
    return v.xyz / v.w;
}

void main()
{
    if (texture(uDepth, vUv).r >= 1.0) { FragColor = vec4(1.0); return; }   // background = lit

    vec3 pos    = viewPos(vUv);
    vec3 normal = normalize(texture(uNormal, vUv).xyz * 2.0 - 1.0);

    vec3 rnd = texture(uNoise, gl_FragCoord.xy / float(textureSize(uNoise, 0).x)).xyz;
    
    vec3 t   = rnd - normal * dot(rnd, normal);
    vec3 tangent = length(t) > 1e-4
        ? t / length(t)
        : normalize(cross(normal, abs(normal.y) < 0.99 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0)));
    
    vec3 bitangent = cross(normal, tangent);
    mat3 tbn       = mat3(tangent, bitangent, normal);

    float occlusion = 0.0;
    for (int i = 0; i < uKernelSize; i++)
    {
        vec3 samplePos = pos + tbn * uKernel[i] * uRadius;     // view space

        vec4 clip = uProjection * vec4(samplePos, 1.0);        // -> screen uv
        clip.xyz /= clip.w;
        
        vec2 sUv  = clip.xy * 0.5 + 0.5;

        float sceneZ = viewPos(sUv).z;                         // geometry depth at that uv

        float rangeCheck = smoothstep(0.0, 1.0, uRadius / max(abs(pos.z - sceneZ), 1e-5));
        occlusion += (sceneZ >= samplePos.z + uBias ? 1.0 : 0.0) * rangeCheck;
    }

    occlusion = clamp(1.0 - occlusion / float(uKernelSize), 0.0, 1.0);
    FragColor = vec4(pow(occlusion, uPower));
}