#version 330 core

in vec3 vLocalPos;
out vec4 FragColor;

const float PI = 3.14159265359;

uniform samplerCube uEnv;
uniform float uMaxRadiance;

void main() {
    vec3 N = normalize(vLocalPos);
    vec3 up = abs(N.y) < 0.999 ? vec3(0,1,0) : vec3(1,0,0);
    vec3 right = normalize(cross(up, N));
    
    up = normalize(cross(N, right));
    
    vec3 irradiance = vec3(0.0);
    float samples = 0.0;
    
    for (float phi = 0.0; phi < 2.0 * PI; phi += 0.025)
    
    for (float theta = 0.0; theta < 0.5 * PI; theta += 0.025) {
        vec3 t = vec3(sin(theta) * cos(phi), sin(theta) * sin(phi), cos(theta));
        vec3 s = t.x * right + t.y * up + t.z * N;

        irradiance += min(texture(uEnv, s).rgb, vec3(uMaxRadiance)) * cos(theta) * sin(theta);
        samples++;
    }
    FragColor = vec4(PI * irradiance / samples, 1.0);
}