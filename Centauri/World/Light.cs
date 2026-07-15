namespace Centauri.World;

using System.Numerics;

public abstract class Light
{
    public Vector3 Color     { get; set; } = Vector3.One;
    public float   Intensity { get; set; } = 1.0f;
    public bool    Enabled   { get; set; } = true;
}

public class DirectionalLight : Light
{
    public Vector3 Direction { get; set; } = new(0f, -1f, 0f);
}

public class PointLight : Light
{
    // position comes from the owning entity's Transform
    public float Constant  { get; set; } = 1.0f;
    public float Linear    { get; set; } = 0.09f;
    public float Quadratic { get; set; } = 0.032f;
}

public class SpotLight : Light
{
    // position comes from the owning entity's Transform; direction stays here
    public Vector3 Direction   { get; set; } = new(0f, -1f, 0f);
    public float   InnerCutoff { get; set; } = 12.5f; // degrees
    public float   OuterCutoff { get; set; } = 17.5f; // degrees

    // Opt-in: shadow-casting spot lights are capped (SpotShadowConfig.MaxShadowSpots) and each
    // one is a real extra render pass, so this stays off by default even with
    // SpotShadowConfig.Enabled on — see Docs/Documentation/LocalShadows.md.
    public bool  CastsShadow { get; set; } = false;

    // Shadow frustum far plane, world units. Distinct from the light's actual illumination
    // falloff (still governed by the constant/linear/quadratic attenuation MainRenderer applies)
    // — this only bounds how far the depth pass's perspective projection reaches, so it wants to
    // roughly match where the light visually stops mattering rather than being tuned precisely.
    public float Range { get; set; } = 25f;
}