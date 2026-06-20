namespace Centauri.Config;

using System.Text.Json.Serialization;
using Silk.NET.Windowing;
using Silk.NET.Input;

public class RenderConfig
{
    // Cache sizes not currently used
    [JsonPropertyName("textureCacheSize")] public int    TextureCacheSize { get; init; } = 128;
    [JsonPropertyName("modelCacheSize")]   public int    ModelCacheSize   { get; init; } = 64;
    [JsonPropertyName("shaderCacheSize")]  public int    ShaderCacheSize  { get; init; } = 32;
    [JsonPropertyName("scenePath")]        public string ScenePath        { get; init; } = "Loading/scene.json";
    [JsonPropertyName("defaultShader")]    public string DefaultShader    { get; init; } = "Assets/Shaders/shaderPBR";
}

public class CameraConfig
{
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

public class ShadowConfig
{
    [JsonPropertyName("enabled")]    public bool Enabled { get; set; } = true;
    [JsonPropertyName("size")]       public uint Size    { get; set; } = 2048;

    [JsonPropertyName("distance")] public float Distance { get; set; }
    [JsonPropertyName("near")]       public float Near       { get; init; } = 1f;
    [JsonPropertyName("far")]        public float Far        { get; init; } = 200f;

    [JsonPropertyName("depthBias")]  public float DepthBias  { get; set; }
    [JsonPropertyName("normalBias")] public float NormalBias { get; set; }
    [JsonPropertyName("pcfRadius")]  public int   PcfRadius  { get; set; }
    
    [JsonPropertyName("cascadeCount")] public int   CascadeCount { get; set; } = 4;    // 2–4 typical
    [JsonPropertyName("splitLambda")]  public float SplitLambda  { get; set; } = 0.85f; // 0=uniform, 1=logarithmic

    [JsonIgnore] public float AuthoredDistance   { get; }
    [JsonIgnore] public float AuthoredDepthBias  { get; }
    [JsonIgnore] public float AuthoredNormalBias { get; }
    [JsonIgnore] public int   AuthoredPcfRadius  { get; }
    [JsonIgnore] public int   AuthoredCascadeCount  { get; }
    [JsonIgnore] public float AuthoredSplitLambda { get; }

    public ShadowConfig(
        float distance   = 50f,
        float depthBias  = 0.0015f,
        float normalBias = 0.02f,
        int   pcfRadius  = 1,
        int cascadeCount = 4,
        float splitLambda = 0.85f)
    {
        Distance   = AuthoredDistance   = distance;
        DepthBias  = AuthoredDepthBias  = depthBias;
        NormalBias = AuthoredNormalBias = normalBias;
        PcfRadius  = AuthoredPcfRadius  = pcfRadius;
        CascadeCount = AuthoredCascadeCount = cascadeCount;
        SplitLambda = AuthoredSplitLambda = splitLambda;
    }
}

public sealed class ColorGrading
{
    [JsonPropertyName("exposure")]   public float Exposure   { get; set; }
    [JsonPropertyName("blackLevel")] public float BlackLevel { get; set; }
    [JsonPropertyName("contrast")]   public float Contrast   { get; set; }
    [JsonPropertyName("saturation")] public float Saturation { get; set; }

    [JsonIgnore] public float AuthoredExposure   { get; }
    [JsonIgnore] public float AuthoredBlackLevel { get; }
    [JsonIgnore] public float AuthoredContrast   { get; }
    [JsonIgnore] public float AuthoredSaturation { get; }

    public ColorGrading(float exposure = 1f, float blackLevel = 0f, float contrast = 1f, float saturation = 1f)
    {
        Exposure   = AuthoredExposure   = exposure;
        BlackLevel = AuthoredBlackLevel = blackLevel;
        Contrast   = AuthoredContrast   = contrast;
        Saturation = AuthoredSaturation = saturation;
    }
}

public class IBLConfig
{
    [JsonPropertyName("iblIntensity")]    public float IblIntensity { get; set; } = 0.3f;
    [JsonPropertyName("maxRadiance")]     public float MaxRadiance { get; init; } = 10f;
    [JsonPropertyName("envSize")]         public uint EnvSize { get; init; } = 512;
    [JsonPropertyName("irradianceSize")]  public uint IrradianceSize { get; init; } = 64;
    [JsonPropertyName("prefilterSize")]   public uint PrefilterSize { get; init; }  = 128;
    [JsonPropertyName("prefilterMips")]   public int PrefilterMips { get; init; }  = 5;
    [JsonPropertyName("brdfSize")]        public uint BrdfSize { get; init; }  = 512;
    
    [JsonIgnore] public float AuthoredIblIntensity { get; }
    
    public IBLConfig(float iblIntensity = 0.3f)
    {
        IblIntensity = AuthoredIblIntensity = iblIntensity;
    }
}
