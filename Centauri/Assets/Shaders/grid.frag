#version 330 core

// ─────────────────────────────────────────────────────────────────────────────
const vec3  GRID_COLOR_COARSE = vec3(0.35, 0.35, 0.35);
const vec3  GRID_COLOR_FINE   = vec3(0.22, 0.22, 0.22);
const float GRID_THICKNESS    = 1.0;
const float GRID_UNIT         = 10.0;

const vec3  AXIS_COLOR_X      = vec3(0.87, 0.17, 0.17);
const vec3  AXIS_COLOR_Z      = vec3(0.17, 0.40, 0.87);
const float AXIS_WIDTH        = 1.0;

const float ALPHA_THRESHOLD   = 0.005;

const float FOG_START         = 50.0;
const float FOG_END           = 200.0;

const float BIAS              = 1e-7;
// ─────────────────────────────────────────────────────────────────────────────

in vec3 fNearPoint;
in vec3 fFarPoint;

uniform mat4 uView;
uniform mat4 uProjection;
uniform vec3 uCameraPos;
uniform vec4 background;

out vec4 FragColor;

float log10(float x)
{
    return log(x) / log(10.0);
}

vec4 Grid(vec3 fragPos, float scale, vec3 gridColor, bool drawAxis)
{
    vec2 coord      = fragPos.xz * scale;
    vec2 derivative = max(fwidth(coord), vec2(1e-6));
    vec2 grid       = abs(fract(coord - 0.5) - 0.5) / derivative;
    float line      = min(grid.x, grid.y);
    float minZ      = min(derivative.y, 1.0);
    float minX      = min(derivative.x, 1.0);

    float alpha = exp(-line * line * 2.0);
    vec4 color  = vec4(gridColor, alpha);

    if (drawAxis)
    {
        // axis in scaled grid-space
        float axisX = smoothstep(derivative.y * AXIS_WIDTH, 0.0, abs(coord.y));
        float axisZ = smoothstep(derivative.x * AXIS_WIDTH, 0.0, abs(coord.x));

        // Z axis (blue)
        if (axisZ > 0.0)
            color = mix(color, vec4(AXIS_COLOR_Z, 1.0), axisZ);

        // X axis (red)
        if (axisX > 0.0)
            color = mix(color, vec4(AXIS_COLOR_X, 1.0), axisX);
    }
    
    return color;
}

float ComputeDepth(vec3 pos)
{
    vec4 clip = uProjection * uView * vec4(pos, 1.0);
    float ndcDepth = clip.z / clip.w;
    
    // convert from [-1,1] to [0,1]
    return ndcDepth * 0.5 + 0.5;
}

float ComputeGridScale()
{
    float dist = length(uCameraPos) / GRID_UNIT;

    float logScale = log10(dist + 1.0);
    return pow(10.0, floor(logScale));
}

float ComputeFade(float scale)
{
    float height = abs(uCameraPos.y) / GRID_UNIT;

    float fadeStart = scale * 2.0;
    float fadeEnd   = scale * 20.0;

    return 1.0 - smoothstep(fadeStart, fadeEnd, height);
}

void main()
{
    float denom = (fFarPoint.y - fNearPoint.y);

    // avoid near-parallel rays
    if (abs(denom) < 1e-5)
        discard;

    float t = -fNearPoint.y / denom;
    if (t < 0.0)
        discard;

    vec3 fragPos = fNearPoint + t * (fFarPoint - fNearPoint);

    if (length(fragPos) > 1e5)
        discard;

    float scale = ComputeGridScale();
    float fade  = ComputeFade(scale);

    gl_FragDepth = min(1.0, ComputeDepth(fragPos) + BIAS);

    vec4 coarse = Grid(fragPos, 1.0 / (scale * GRID_UNIT),  GRID_COLOR_COARSE, true);
    vec4 fine   = Grid(fragPos, 10.0 / (scale * GRID_UNIT), GRID_COLOR_FINE,  false);

    FragColor = vec4(max(coarse, fine).rgb, max(coarse, fine).a * fade);
    
    // distance-based fog
    float dist = length(fragPos - uCameraPos);
    float fogFactor = smoothstep(FOG_START, FOG_END, dist);
    
    FragColor = mix(FragColor, background, fogFactor);
    
    // FragColor = vec4(1, 0, 0, 1);

    if (FragColor.a < ALPHA_THRESHOLD) discard;
}