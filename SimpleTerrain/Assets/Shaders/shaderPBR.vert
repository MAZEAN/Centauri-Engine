#version 330 core
layout (location = 0) in vec3 vPos;
layout (location = 1) in vec3 vNormal;
layout (location = 2) in vec2 vUv;

uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProjection;
uniform vec2 uUvScale;
uniform vec2 uUvOffset;

out vec2 fUv;
out vec3 fNormal;
out vec3 fFragPos;
out mat3 fTBN;      // tangent space matrix for normal mapping

void main()
{
    gl_Position = uProjection * uView * uModel * vec4(vPos, 1.0);
    fUv         = vUv * uUvScale + uUvOffset;
    fFragPos    = vec3(uModel * vec4(vPos, 1.0));

    mat3 normalMatrix = mat3(transpose(inverse(uModel)));
    fNormal           = normalize(normalMatrix * vNormal);
}