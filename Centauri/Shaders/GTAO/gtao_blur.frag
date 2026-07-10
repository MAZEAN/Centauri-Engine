#version 330 core

in  vec2 vUv;

out vec4 FragColor;

// ─────────────────────────────────────────────────────────────────────────────

uniform sampler2D uGtao;
uniform sampler2D uDepth;
uniform mat4      uInvProjection;

// ─────────────────────────────────────────────────────────────────────────────

// reconstructs view-space Z only, for the depth-similarity weight below (matches gtao.frag's
// viewPos() convention: depth is [0,1], remapped to NDC [-1,1] before unprojecting)
float viewZ(vec2 uv)
{
    float d   = texture(uDepth, uv).r;
    vec4  ndc = vec4(uv * 2.0 - 1.0, d * 2.0 - 1.0, 1.0);
    vec4  v   = uInvProjection * ndc;

    return v.z / v.w;
}

// 4x4 box blur — exactly the noise tile size, so it averages out the rotation pattern — but
// depth-aware: taps whose view-space depth diverges too far from the center pixel are excluded,
// so the filter doesn't bleed AO across silhouette edges the way a plain box blur does.
void main()
{
    vec2 texel = 1.0 / vec2(textureSize(uGtao, 0));
    
    if (texture(uDepth, vUv).r >= 1.0) 
    { 
        FragColor = vec4(1.0); 
        return; 
    }

    float centerZ         = viewZ(vUv);
    float depthThreshold   = abs(centerZ) * 0.05;

    float sum = 0.0;
    float weightSum = 0.0;
    for (int x = -2; x < 2; x++)
    {
        for (int y = -2; y < 2; y++)
        {
            vec2 uv = vUv + vec2(x, y) * texel;
            if (texture(uDepth, uv).r >= 1.0) continue;

            float weight = abs(viewZ(uv) - centerZ) < depthThreshold ? 1.0 : 0.0;
            sum += texture(uGtao, uv).r * weight;
            weightSum += weight;
        }    
    }
    
    FragColor = vec4(weightSum > 0.0 ? sum / weightSum : texture(uGtao, vUv).r);
}