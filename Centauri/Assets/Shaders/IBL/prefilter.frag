#version 330 core

in vec3 vLocalPos;
out vec4 FragColor;

const float PI = 3.14159265359;

uniform samplerCube uEnv;
uniform float uRoughness;
uniform float uResolution;     // env face size, for mip selection

float RadicalInverse_VdC(uint bits) {
    bits = (bits<<16u) | (bits>>16u);
    bits = ((bits&0x55555555u)<<1u) | ((bits&0xAAAAAAAAu)>>1u);
    bits = ((bits&0x33333333u)<<2u) | ((bits&0xCCCCCCCCu)>>2u);
    bits = ((bits&0x0F0F0F0Fu)<<4u) | ((bits&0xF0F0F0F0u)>>4u);
    bits = ((bits&0x00FF00FFu)<<8u) | ((bits&0xFF00FF00u)>>8u);
    
    return float(bits) * 2.3283064365386963e-10;
}

vec2 Hammersley(uint i, uint N) {
    return vec2(float(i) / float(N), RadicalInverse_VdC(i));
}

vec3 ImportanceSampleGGX(vec2 Xi,vec3 N,float r) {
    float a = r * r; 
    float phi = 2.0 * PI * Xi.x;
    float ct = sqrt((1.0 - Xi.y) / (1.0 + (a * a - 1.0) * Xi.y)); 
    float st = sqrt(1.0 - ct * ct);
    
    vec3 H = vec3(cos(phi) * st, sin(phi) * st,ct);
    vec3 up = abs(N.z) < 0.999 ? vec3(0, 0, 1) : vec3(1, 0, 0);
    vec3 tx = normalize(cross(up, N)); 
    vec3 ty = cross(N, tx);
    
    return normalize(tx * H.x + ty * H.y + N*H.z);
}

float D_GGX(vec3 N, vec3 H, float r) {
    float a = r * r;
    float a2 = a * a;
    float nh = max(dot(N, H), 0.0);
    float d = (nh * nh * (a2 - 1.0) + 1.0);
    
    return a2 / (PI * d * d);
}

void main() {
    vec3 N = normalize(vLocalPos); 
    vec3 V = N;
    
    const uint S = 1024u; 
    
    vec3 acc = vec3(0.0); 
    float w = 0.0;
    
    for(uint i = 0u; i < S; i++){
        vec2 Xi = Hammersley(i, S);
        vec3 H = ImportanceSampleGGX(Xi, N, uRoughness);
        vec3 L = normalize(2.0 * dot(V, H) * H - V);
        
        float nl = max(dot(N, L), 0.0);
        
        if (nl > 0.0){
            float d = D_GGX(N, H, uRoughness); 
            float nh = max(dot(N, H), 0.0); 
            float hv = max(dot(H, V), 0.0);
            
            float pdf = d * nh / (4.0 * hv) + 0.0001;
            float saT = 4.0 * PI / (6.0 * uResolution * uResolution);
            float saS = 1.0 / (float(S) * pdf + 0.0001);
            
            float mip = uRoughness == 0.0 ? 0.0 : 0.5 * log2(saS / saT);
            
            acc += textureLod(uEnv, L, mip).rgb * nl;
            w+=nl;
        }
    }
    FragColor = vec4(acc/ w, 1.0);
}