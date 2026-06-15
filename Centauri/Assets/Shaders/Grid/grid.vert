#version 330 core
layout (location = 0) in vec3 vPos;

uniform mat4 uView;
uniform mat4 uProjection;

out vec3 fNearPoint;
out vec3 fFarPoint;

vec3 UnprojectPoint(float x, float y, float z, mat4 view, mat4 projection)
{
    mat4 viewInv = inverse(view);
    mat4 projInv = inverse(projection);
    vec4 unprojected = viewInv * projInv * vec4(x, y, z, 1.0);
    return unprojected.xyz / unprojected.w;
}

void main()
{
    fNearPoint = UnprojectPoint(vPos.x, vPos.y, 0.0, uView, uProjection);
    fFarPoint  = UnprojectPoint(vPos.x, vPos.y, 1.0, uView, uProjection);
    gl_Position = vec4(vPos, 1.0);
}