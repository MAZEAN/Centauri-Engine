#version 330 core

in  vec3 vDir;

out vec4 FragColor;

const vec2  invAtan = vec2(0.1591549, 0.3183099); // 1/(2π), 1/π
const float PI      = 3.14159265;

uniform sampler2D uPanorama;
uniform int   uHdr;         // 1 = linear HDR radiance, 0 = display-ready sRGB LDR
uniform float uExposure;    // pre-tonemap multiplier (HDR only)
uniform float uBlackLevel;  // crush radiance below this to black (HDR only)

uniform int   uMode;        // 0 = sample panorama, 1 = procedural Preetham atmosphere

uniform vec3  uSunDir;          // world-space direction from the sky toward the sun
uniform vec3  uSunColor;        // sun disc radiance
uniform float uSunAngularSize;  // cosine of the disc's half-angle
uniform float uSunGlowExponent; // higher = tighter halo around the disc

// Procedural sky (mode 1)
uniform float uTurbidity;   // atmospheric haziness (1 clear .. 6+ hazy)
uniform float uSkyIntensity; // scales relative sky radiance into the exposure/tonemap range

// ── Preetham et al. "A Practical Analytic Model for Daylight" ────────────────────
// The Perez luminance/chromaticity distribution: a 5-coefficient angular falloff whose
// coefficients are linear in turbidity. We use the RELATIVE form (Yz = 1) rather than the
// model's absolute cd/m² zenith luminance — absolute values are in the thousands and would
// blow straight past this engine's exposure/tonemap pipeline; uSkyIntensity places the
// normalized result back into range instead.
float perez(float cosTheta, float gamma, float A, float B, float C, float D, float E)
{
    float cg = cos(gamma);
    return (1.0 + A * exp(B / max(cosTheta, 0.01)))
    * (1.0 + C * exp(D * gamma) + E * cg * cg);
}

vec3 preethamSky(vec3 dir, vec3 sunDir, float turbidity)
{
    float T = turbidity;

    // Perez coefficients (linear in turbidity), for luminance Y and chromaticity x, y.
    float AY =  0.1787 * T - 1.4630, BY = -0.3554 * T + 0.4275, CY = -0.0227 * T + 5.3251,
          DY =  0.1206 * T - 2.5771, EY = -0.0670 * T + 0.3703;
    float Ax = -0.0193 * T - 0.2592, Bx = -0.0665 * T + 0.0008, Cx = -0.0004 * T + 0.2125,
          Dx = -0.0641 * T - 0.8989, Ex = -0.0033 * T + 0.0452;
    float Ay = -0.0167 * T - 0.2608, By = -0.0950 * T + 0.0092, Cy = -0.0079 * T + 0.2102,
          Dy = -0.0441 * T - 1.6537, Ey = -0.0109 * T + 0.0529;

    float thetaS  = acos(clamp(sunDir.y, 0.0, 1.0));   // sun angle from zenith
    float cosTheta = max(dir.y, 0.0);                  // view angle from zenith (cos)
    float gamma    = acos(clamp(dot(dir, sunDir), -1.0, 1.0));  // view-to-sun angle

    // Zenith chromaticity as a polynomial in (thetaS, turbidity).
    float t2 = thetaS * thetaS, t3 = t2 * thetaS, T2 = T * T;
    float xz = ( 0.00166 * t3 - 0.00375 * t2 + 0.00209 * thetaS)            * T2
    + (-0.02903 * t3 + 0.06377 * t2 - 0.03202 * thetaS + 0.00394) * T
    + ( 0.11693 * t3 - 0.21196 * t2 + 0.06052 * thetaS + 0.25886);
    float yz = ( 0.00275 * t3 - 0.00610 * t2 + 0.00317 * thetaS)            * T2
    + (-0.04214 * t3 + 0.08970 * t2 - 0.04153 * thetaS + 0.00516) * T
    + ( 0.15346 * t3 - 0.26756 * t2 + 0.06670 * thetaS + 0.26688);

    // Relative luminance + chromaticity, each normalized against the zenith-toward-sun value.
    float Y = perez(cosTheta, gamma, AY, BY, CY, DY, EY) / perez(1.0, thetaS, AY, BY, CY, DY, EY);
    float x = xz * perez(cosTheta, gamma, Ax, Bx, Cx, Dx, Ex) / perez(1.0, thetaS, Ax, Bx, Cx, Dx, Ex);
    float y = yz * perez(cosTheta, gamma, Ay, By, Cy, Dy, Ey) / perez(1.0, thetaS, Ay, By, Cy, Dy, Ey);

    // xyY → XYZ → linear sRGB.
    vec3 XYZ = vec3(x / y * Y, Y, (1.0 - x - y) / y * Y);
    vec3 rgb = mat3( 3.2406, -0.9689,  0.0557,
            -1.5372,  1.8758, -0.2040,
            -0.4986,  0.0415,  1.0570) * XYZ;

    return max(rgb, vec3(0.0));
}

void main()
{
    vec3 d  = normalize(vDir);
    vec2 uv = vec2(atan(d.z, d.x), asin(clamp(d.y, -1.0, 1.0))) * invAtan + 0.5;
    vec3 color = texture(uPanorama, uv).rgb;

    if (uMode == 1)
    {
        color = preethamSky(d, uSunDir, uTurbidity) * uSkyIntensity;

        // Fade to a dim night blue as the sun sinks (Preetham is undefined below the horizon).
        // Tracks the same elevation DayNightCycle uses to fade the light, so sky + lighting
        // dim together without a separate day/night branch on the CPU.
        float day = smoothstep(-0.08, 0.12, uSunDir.y);
        color = mix(vec3(0.008, 0.012, 0.025), color, day);
    }
    else {
        vec2 uv = vec2(atan(d.z, d.x), asin(clamp(d.y, -1.0, 1.0))) * invAtan + 0.5;
        color = texture(uPanorama, uv).rgb;

        if (uHdr == 1)
            color *= uExposure;             // HDR: linear radiance, normalize brightness
        else
            color = pow(color, vec3(2.2));  // LDR sRGB → linear so the post pass grades it too

        color = max(color - uBlackLevel, vec3(0.0));   // crush the sky's faint floor to black
    }
    
    float cosAngle = dot(d, uSunDir);
    float disc     = smoothstep(uSunAngularSize - 0.0006, uSunAngularSize, cosAngle);
    float glow     = pow(max(cosAngle, 0.0), uSunGlowExponent);
    color += uSunColor * (disc * 6.0 + glow * 0.4);

    FragColor = vec4(color, 1.0);       // linear HDR — global grade + tonemap happen in post
}