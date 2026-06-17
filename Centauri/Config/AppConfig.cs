namespace Centauri.Config;

using System.Text.Json.Serialization;
using Silk.NET.Windowing;
using Silk.NET.Input;

public class AppConfig
{
    [JsonPropertyName("window")] public WindowConfig Window { get; init; } = new();
    [JsonPropertyName("camera")] public CameraConfig Camera { get; init; } = new();
    [JsonPropertyName("render")] public RenderConfig Render { get; init; } = new();
    [JsonPropertyName("imGui")]  public ImGuiConfig  ImGui  { get; init; } = new();
    [JsonPropertyName("input")]  public InputConfig  Input  { get; init; } = new();
    [JsonPropertyName("debug")]  public DebugConfig  Debug  { get; init; } = new();
    [JsonPropertyName("grading")] public GradingConfig Grading { get; init; } = new();
}

public class RenderConfig
{
    // Cache sizes not currently used
    [JsonPropertyName("textureCacheSize")] public int    TextureCacheSize { get; init; } = 128;
    [JsonPropertyName("modelCacheSize")]   public int    ModelCacheSize   { get; init; } = 64;
    [JsonPropertyName("shaderCacheSize")]  public int    ShaderCacheSize  { get; init; } = 32;
    [JsonPropertyName("scenePath")]        public string ScenePath        { get; init; } = "Loading/scene.json";
    [JsonPropertyName("defaultShader")]    public string DefaultShader    { get; init; } = "Assets/Shaders/shaderPBR";
    [JsonPropertyName("iblIntensity")]     public float IblIntensity { get; init; } = 0.3f;
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
    [JsonPropertyName("panSensitivity")]  public float PanSensitivity  { get; init; } = 0.01f;
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

public class DebugConfig
{
    [JsonPropertyName("enableCulling")]     public bool EnableCulling     { get; set; } = true;
    [JsonPropertyName("showDebugView")]     public bool ShowDebugView     { get; set; } = false;
    [JsonPropertyName("showBoundingBoxes")] public bool ShowBoundingBoxes { get; set; } = false;
    [JsonPropertyName("showFrustums")]      public bool ShowFrustums      { get; set; } = false;
    [JsonPropertyName("showCameras")]       public bool ShowCameras       { get; set; } = false;
    [JsonPropertyName("showGrid")]          public bool ShowGrid          { get; set; } = false;
    [JsonPropertyName("showStatsOverlay")]  public bool ShowStatsOverlay  { get; set; } = true;
    [JsonPropertyName("showSkybox")]        public bool ShowSkybox        { get; set; } = true;

    public void ToggleShowDebugView()
    {
        ShowDebugView = !ShowDebugView;
        
        ShowBoundingBoxes = ShowFrustums = ShowCameras = ShowDebugView;
    }

    public void ToggleEnableCulling()     => EnableCulling     = !EnableCulling;
    public void ToggleShowBoundingBoxes() => ShowBoundingBoxes = !ShowBoundingBoxes;
    public void ToggleShowFrustums()      => ShowFrustums      = !ShowFrustums;
    public void ToggleShowCameras()       => ShowCameras       = !ShowCameras;
    public void ToggleShowGrid()          => ShowGrid          = !ShowGrid;
    public void ToggleShowStatsOverlay()  => ShowStatsOverlay = !ShowStatsOverlay;
    public void ToggleShowSkybox()        => ShowSkybox = !ShowSkybox;
}

public enum ViewMode { Fly, Edit }

public class InputConfig
{
    [JsonPropertyName("mode")] public ViewMode Mode { get; set; } = ViewMode.Fly;
    [JsonPropertyName("toggleModeKey")] public Key ToggleModeKey { get; init; } = Key.Tab;

    public void ToggleMode()
    {
        Mode = Mode == ViewMode.Fly ? ViewMode.Edit : ViewMode.Fly;
    }
}

public class GradingConfig
{
    [JsonPropertyName("exposure")]   public float Exposure   { get; init; } = 1.0f;
    [JsonPropertyName("blackLevel")] public float BlackLevel { get; init; } = 0.0f;
    [JsonPropertyName("contrast")]   public float Contrast   { get; init; } = 1.0f;
    [JsonPropertyName("saturation")] public float Saturation { get; init; } = 1.0f;
}