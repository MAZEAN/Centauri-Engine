namespace Centauri.Config;

using System.Text.Json.Serialization;
using Silk.NET.Windowing;

public class AppConfig
{
    [JsonPropertyName("window")] public WindowConfig Window { get; init; } = new();
    [JsonPropertyName("camera")] public CameraConfig Camera { get; init; } = new();
    [JsonPropertyName("render")] public RenderConfig Render { get; init; } = new();
    [JsonPropertyName("imGui")]  public ImGuiConfig  ImGui  { get; init; } = new();
    
}

public class RenderConfig
{
    // Cache sizes not currently used
    [JsonPropertyName("textureCacheSize")] public int    TextureCacheSize { get; init; } = 128;
    [JsonPropertyName("modelCacheSize")]   public int    ModelCacheSize   { get; init; } = 64;
    [JsonPropertyName("shaderCacheSize")]  public int    ShaderCacheSize  { get; init; } = 32;
    [JsonPropertyName("scenePath")]        public string ScenePath        { get; init; } = "Assets/scene.json";
}

public class CameraConfig
{
    // global camera tuning — per-camera placement/roles live in the scene
    [JsonPropertyName("fov")]             public float FOV             { get; init; } = 45f;
    [JsonPropertyName("near")]            public float Near            { get; init; } = 0.1f;
    [JsonPropertyName("far")]             public float Far             { get; init; } = 1000f;
    [JsonPropertyName("sensitivityX")]    public float SensitivityX    { get; init; } = 0.1f;
    [JsonPropertyName("sensitivityY")]    public float SensitivityY    { get; init; } = 0.1f;
    [JsonPropertyName("zoomSensitivity")] public float ZoomSensitivity { get; init; } = 1.5f;
    [JsonPropertyName("minZoom")]         public float MinZoom         { get; init; } = 1.0f;
    [JsonPropertyName("maxZoom")]         public float MaxZoom         { get; init; } = 45.0f;
    [JsonPropertyName("moveSpeed")]       public float MoveSpeed       { get; init; } = 2.5f;
}

public class WindowConfig
{
    [JsonPropertyName("title")]       public string      Title       { get; init; } = "Centauri";
    [JsonPropertyName("windowState")] public WindowState WindowState { get; init; } = WindowState.Maximized;
    [JsonPropertyName("enableVSync")] public bool        EnableVSync { get; init; } = true;
    [JsonPropertyName("samples")]     public int         Samples     { get; init; } = 4;
    [JsonPropertyName("clearColor")]  public float[]     ClearColor  { get; init; } = [1.0f, 1.0f, 1.0f, 1.0f];
}

public class ImGuiConfig
{
    [JsonPropertyName("font")] public string Font { get; init; } = "Assets/Fonts/IosevkaCharon-Regular.ttf";
    [JsonPropertyName("fontSize")] public float FontSize { get; init; } = 20f;
}