namespace Centauri.Rendering;

// Global tone / color-grading applied once in the tonemap pass. Live values are
// edited from the inspector; the Authored* values (seeded from config) are the
// right-click reset targets, mirroring Entity.Authored / Skybox.AuthoredExposure.
public sealed class ColorGrading
{
    public float Exposure   { get; set; }   // linear multiplier  (pre-tonemap)
    public float BlackLevel { get; set; }   // floor lifted to black (pre-tonemap)
    public float Contrast   { get; set; }   // 1 = neutral  (post-tonemap)
    public float Saturation { get; set; }   // 1 = neutral, 0 = grayscale

    public float AuthoredExposure   { get; }
    public float AuthoredBlackLevel { get; }
    public float AuthoredContrast   { get; }
    public float AuthoredSaturation { get; }

    public ColorGrading(float exposure = 1f, float blackLevel = 0f, float contrast = 1f, float saturation = 1f)
    {
        Exposure   = AuthoredExposure   = exposure;
        BlackLevel = AuthoredBlackLevel = blackLevel;
        Contrast   = AuthoredContrast   = contrast;
        Saturation = AuthoredSaturation = saturation;
    }
}