namespace SimpleTerrain.Config;

using System.Numerics;
using Silk.NET.Windowing;

public class AppConfig
{
    public WindowConfig Window { get; init; } = new();
    public CameraConfig Camera { get; init; } = new();
    public RenderConfig Render { get; init; } = new();
    public float MoveSpeed { get; init; } = 2.5f;
}

public class RenderConfig
{
    public int TextureCacheSize { get; init; } = 128;
    public int ModelCacheSize { get; init; } = 64;
    public int ShaderCacheSize { get; init; } = 32;
    public string ScenePath { get; init; } = "Assets/scene.json";
}

public class CameraConfig
{
    public string PrimaryCamera { get; init; } = "Main";
    public float FOV { get; init; } = 45f;
    public float Near { get; init; } = 0.1f;
    public float Far { get; init; } = 1000f;
    public float SensitivityX { get; init; } = 0.1f;
    public float SensitivityY { get; init; } = 0.1f;
    public float ZoomSensitivity { get; init; } = 1.5f;
    public float MinZoom { get; init; } = 1.0f;
    public float MaxZoom { get; init; } = 45.0f;
}

public class WindowConfig
{
    public string Title { get; init; } = "SimpleTerrain";
    public WindowState WindowState { get; init; } = WindowState.Maximized;
    public bool EnableVSync { get; init; } = true;
    public int Samples { get; init; } = 4;
    public float[] ClearColor { get; init; } = [1.0f, 1.0f, 1.0f, 1.0f];
}