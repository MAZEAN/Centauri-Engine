#version 330 core

in  vec2 vUv;
out vec4 FragColor;

uniform sampler2D uNormal;   // view-space normals, encoded [0,1]
uniform sampler2D uDepth;    // non-linear depth [0,1]
uniform sampler2D uAo;       // screen-space AO (R)
uniform sampler2D uVelocity; // TAA motion vectors (uv delta, RG)

uniform int   uMode;         // 1 = normals, 2 = depth
uniform float uNear;
uniform float uFar;

void main()
{
    if (uMode == 1)
    {
        FragColor = vec4(texture(uNormal, vUv).rgb, 1.0);   // already display-ready
    }
    else if (uMode == 3)
    {
        FragColor = vec4(vec3(texture(uAo, vUv).r), 1.0);
    }
    else if (uMode == 4)
    {
        vec2 v = texture(uVelocity, vUv).xy * 20.0;
        FragColor = vec4(0.5 + v.x, 0.5 + v.y, 0.5, 1.0);
    }
    else
    {
        float d = texture(uDepth, vUv).r;
        float z = (uNear * uFar) / (uFar - d * (uFar - uNear));
        float g = z / uFar;
        
        FragColor = vec4(vec3(g), 1.0);
    }
}
