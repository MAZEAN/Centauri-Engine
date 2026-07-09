#version 330 core

in vec2 vUv;

out vec4 FragColor;

// ─────────────────────────────────────────────────────────────────────────────

const float PI = 3.14159265359;

// ─────────────────────────────────────────────────────────────────────────────

float RadicalInverse_VdC(uint bits) {
    bits = (bits << 16u) | (bits >> 16u);
    bits = ((bits & 0x55555555u) << 1u) | ((bits & 0xAAAAAAAAu) >> 1u);
    bits = ((bits & 0x33333333u) << 2u) | ((bits & 0xCCCCCCCCu) >> 2u);
    bits = ((bits & 0x0F0F0F0Fu) << 4u) | ((bits & 0xF0F0F0F0u) >> 4u);
    bits = ((bits & 0x00FF00FFu) << 8u) | ((bits & 0xFF00FF00u) >> 8u);
    
    return float(bits) * 2.3283064365386963e-10;
}

vec2 Hammersley(uint i, uint N) {
    return vec2(float(i) / float(N), RadicalInverse_VdC(i));
}

vec3 ImportanceSampleGGX(vec2 Xi, vec3 N, float r) {
    float a = r * r;
    float phi = 2.0 * PI * Xi.x;
    float ct = sqrt((1.0 - Xi.y) / (1.0 + (a * a - 1.0) * Xi.y));
    float st = sqrt(1.0 - ct * ct);

    vec3 H = vec3(cos(phi) * st, sin(phi) * st,ct);
    
    vec3 up = abs(N.z) < 0.999 ? vec3(0, 0, 1) : vec3(1, 0, 0);
    
    vec3 tx = normalize(cross(up, N));
    vec3 ty = cross(N, tx);

    return normalize(tx * H.x + ty * H.y + N * H.z);
}

float G_SchlickGGX(float nv, float r) {
    float k = (r * r) / 2.0;
    return nv / (nv * (1.0 - k) + k);
}

float G_Smith(vec3 N, vec3 V, vec3 L, float r) {
    return G_SchlickGGX(max(dot(N, V), 0.0), r) * G_SchlickGGX(max(dot(N, L), 0.0), r);
}

vec2 Integrate(float NdotV, float r) {
    vec3 V = vec3(sqrt(1.0 - NdotV*NdotV), 0.0, NdotV);
    vec3 N = vec3(0 ,0, 1);
    
    float A = 0.0, B = 0.0;
    const uint S = 1024u;
    
    for (uint i = 0u; i < S; i++) {
        vec2 Xi = Hammersley(i, S);
        vec3 H = ImportanceSampleGGX(Xi, N, r); 
        vec3 L = normalize(2.0 * dot(V, H) * H - V);
        
        float nl = max(L.z, 0.0);
        float nh = max(H.z, 0.0);
        float vh = max(dot(V, H), 0.0);
        
        if (nl > 0.0) {
            float G = G_Smith(N, V, L, r);
            float Gv = (G * vh) / (nh * NdotV);
            float Fc = pow(1.0 - vh, 5.0);
            
            A += (1.0 - Fc) * Gv; B += Fc * Gv; 
        }
    }
    return vec2(A, B) / float(S);
}
void main() { 
    FragColor = vec4(Integrate(clamp(vUv.x, 0.001, 1.0), vUv.y), 0.0, 1.0); 
}