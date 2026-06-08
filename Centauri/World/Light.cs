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
    public Vector3 Position  { get; set; } = Vector3.Zero;
    public float   Constant  { get; set; } = 1.0f;
    public float   Linear    { get; set; } = 0.09f;
    public float   Quadratic { get; set; } = 0.032f;
}

public class SpotLight : Light
{
    public Vector3 Position    { get; set; } = Vector3.Zero;
    public Vector3 Direction   { get; set; } = new(0f, -1f, 0f);
    public float   InnerCutoff { get; set; } = 12.5f; // degrees
    public float   OuterCutoff { get; set; } = 17.5f; // degrees
}