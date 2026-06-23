#version 330 core

in  vec2 vUv;
out vec4 FragColor;

uniform sampler2D uNormal;   // view-space normals, encoded [0,1]
uniform sampler2D uDepth;    // non-linear depth [0,1]
uniform sampler2D uAo;       // screen-space AO (R)
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
    else
    {
        float d = texture(uDepth, vUv).r;
        // reverse a [0,1] perspective depth back to linear view-space distance
        float z = (uNear * uFar) / (uFar - d * (uFar - uNear));
        float g = z / uFar;                                 // normalize to [near/far, 1]
        
        FragColor = vec4(vec3(g), 1.0);
    }
}
