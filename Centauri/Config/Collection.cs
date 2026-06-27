namespace Centauri.Config;

using System.Numerics;
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
    [JsonPropertyName("title")]       public string Title { get; init; } = "Centauri";
    [JsonPropertyName("state")]       public WindowState State { get; init; } = WindowState.Maximized;
    [JsonPropertyName("border")]      public WindowBorder Border { get; init; } = WindowBorder.Hidden;
    [JsonPropertyName("enableVSync")] public bool EnableVSync { get; init; } = true;
    [JsonPropertyName("samples")]     public int Samples { get; init; } = 4;
    [JsonPropertyName("clearColor")]  public float[] ClearColor { get; init; } = [1.0f, 1.0f, 1.0f, 1.0f];
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
    [JsonPropertyName("showGPUTimings")]    public bool ShowGPUTimings    { get; set; } = true;
    [JsonPropertyName("showAnisotropicFilter")] public bool AnisotropicFilter { get; set; } = true;
    [JsonIgnore] public ShadingMode Shading { get; set; } = ShadingMode.Shaded;

    public void ToggleShowStatsOverlay()  => ShowStatsOverlay = !ShowStatsOverlay;
    public void CycleShading() =>
        Shading = (ShadingMode)(((int)Shading + 1) % Enum.GetValues<ShadingMode>().Length);
}

public enum ShadingMode { Shaded, Normals, Depth, AmbientOcclusion, Velocity }

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

public class ShadowConfig : IJsonOnDeserialized
{
    public readonly int MaxCascades = 4;

    [JsonPropertyName("enabled")]      public bool  Enabled      { get; set; } = true;
    [JsonPropertyName("size")]         public uint  Size         { get; set; } = 4096;
    [JsonPropertyName("distance")]     public float Distance     { get; set; } = 150f;
    [JsonPropertyName("depthBias")]    public float DepthBias    { get; set; } = 0.0005f;
    [JsonPropertyName("normalBias")]   public float NormalBias   { get; set; } = 1.5f;
    [JsonPropertyName("pcfRadius")]    public int   PcfRadius    { get; set; } = 2;
    [JsonPropertyName("cascadeCount")] public int   CascadeCount { get; set; } = 4;   // 2–4 typical
    [JsonPropertyName("splitLambda")]  public float SplitLambda  { get; set; } = 0.85f; // 0=uniform, 1=log

    [JsonIgnore] public bool DebugCascades { get; set; }

    [JsonIgnore] public uint  AuthoredSize         { get; private set; }
    [JsonIgnore] public float AuthoredDistance     { get; private set; }
    [JsonIgnore] public float AuthoredDepthBias    { get; private set; }
    [JsonIgnore] public float AuthoredNormalBias   { get; private set; }
    [JsonIgnore] public int   AuthoredPcfRadius    { get; private set; }
    [JsonIgnore] public int   AuthoredCascadeCount { get; private set; }
    [JsonIgnore] public float AuthoredSplitLambda  { get; private set; }

    public ShadowConfig() => OnDeserialized();

    public void OnDeserialized()
    {
        AuthoredSize         = Size;
        AuthoredDistance     = Distance;
        AuthoredDepthBias    = DepthBias;
        AuthoredNormalBias   = NormalBias;
        AuthoredPcfRadius    = PcfRadius;
        AuthoredCascadeCount = CascadeCount;
        AuthoredSplitLambda  = SplitLambda;
    }
}

public sealed class ColorGrading : IJsonOnDeserialized
{
    [JsonPropertyName("exposure")]   public float Exposure   { get; set; } = 1f;
    [JsonPropertyName("blackLevel")] public float BlackLevel { get; set; } = 0f;
    [JsonPropertyName("contrast")]   public float Contrast   { get; set; } = 1f;
    [JsonPropertyName("saturation")] public float Saturation { get; set; } = 1f;

    [JsonIgnore] public float AuthoredExposure   { get; private set; }
    [JsonIgnore] public float AuthoredBlackLevel { get; private set; }
    [JsonIgnore] public float AuthoredContrast   { get; private set; }
    [JsonIgnore] public float AuthoredSaturation { get; private set; }

    public ColorGrading() => OnDeserialized();

    public void OnDeserialized()
    {
        AuthoredExposure   = Exposure;
        AuthoredBlackLevel = BlackLevel;
        AuthoredContrast   = Contrast;
        AuthoredSaturation = Saturation;
    }
}

public class IBLConfig : IJsonOnDeserialized
{
    [JsonPropertyName("iblIntensity")]    public float IblIntensity { get; set; } = 0.3f;
    [JsonPropertyName("maxRadiance")]     public float MaxRadiance { get; init; } = 10f;
    [JsonPropertyName("envSize")]         public uint EnvSize { get; init; } = 512;
    [JsonPropertyName("irradianceSize")]  public uint IrradianceSize { get; init; } = 64;
    [JsonPropertyName("prefilterSize")]   public uint PrefilterSize { get; init; }  = 128;
    [JsonPropertyName("prefilterMips")]   public int PrefilterMips { get; init; }  = 5;
    [JsonPropertyName("brdfSize")]        public uint BrdfSize { get; init; }  = 512;
    
    [JsonIgnore] public float AuthoredIblIntensity { get; private set; }
    
    public IBLConfig() => OnDeserialized();

    public void OnDeserialized()
    {
        AuthoredIblIntensity =  IblIntensity;
    }
}

public class SSAOConfig : IJsonOnDeserialized
{
    [JsonPropertyName("enabled")]     public bool  Enabled     { get; set; } = true;
    [JsonPropertyName("radius")]      public float Radius      { get; set; } = 0.5f;
    [JsonPropertyName("bias")]        public float Bias        { get; set; } = 0.025f;
    [JsonPropertyName("power")]       public float Power       { get; set; } = 1.5f;
    [JsonPropertyName("sampleCount")] public int   SampleCount { get; set; } = 32;

    [JsonIgnore] public float AuthoredRadius      { get; private set; }
    [JsonIgnore] public float AuthoredBias        { get; private set; }
    [JsonIgnore] public float AuthoredPower       { get; private set; }
    [JsonIgnore] public int   AuthoredSampleCount { get; private set; }

    public SSAOConfig() => OnDeserialized();

    public void OnDeserialized()
    {
        AuthoredRadius      = Radius;
        AuthoredBias        = Bias;
        AuthoredPower       = Power;
        AuthoredSampleCount = SampleCount;
    }
}

public sealed class BloomConfig : IJsonOnDeserialized
{
    [JsonPropertyName("enabled")]   public bool  Enabled   { get; set; } = true;
    [JsonPropertyName("threshold")] public float Threshold { get; set; } = 1.0f;   // luma above which pixels bloom
    [JsonPropertyName("knee")]      public float Knee      { get; set; } = 0.5f;   // soft shoulder around the threshold
    [JsonPropertyName("intensity")] public float Intensity { get; set; } = 0.5f;   // additive strength into the scene
    [JsonPropertyName("radius")]    public float Radius    { get; set; } = 1.0f;   // upsample filter spread

    [JsonIgnore] public float AuthoredThreshold { get; private set; }
    [JsonIgnore] public float AuthoredKnee      { get; private set; }
    [JsonIgnore] public float AuthoredIntensity { get; private set; }
    [JsonIgnore] public float AuthoredRadius    { get; private set; }

    public BloomConfig() => OnDeserialized();

    public void OnDeserialized()
    {
        AuthoredThreshold = Threshold;
        AuthoredKnee      = Knee;
        AuthoredIntensity = Intensity;
        AuthoredRadius    = Radius;
    }
}

public sealed class TAAConfig : IJsonOnDeserialized
{
    [JsonPropertyName("enabled")]  public bool  Enabled  { get; set; } = false;
    [JsonPropertyName("feedback")] public float Feedback { get; set; } = 0.9f;

    [JsonIgnore] public float AuthoredFeedback { get; private set; }

    public TAAConfig() => OnDeserialized();

    public void OnDeserialized() => AuthoredFeedback = Feedback;
}

public sealed class WindConfig : IJsonOnDeserialized
{
    [JsonPropertyName("enabled")]   public bool  Enabled   { get; set; } = true;
    [JsonPropertyName("strength")]  public float Strength  { get; set; } = 0.06f;   // sway amplitude
    [JsonPropertyName("speed")]     public float Speed     { get; set; } = 1.6f;    // oscillation frequency
    [JsonPropertyName("direction")] public float Direction { get; set; } = 37f;     // heading in XZ, degrees

    [JsonIgnore] public float AuthoredStrength  { get; private set; }
    [JsonIgnore] public float AuthoredSpeed     { get; private set; }
    [JsonIgnore] public float AuthoredDirection { get; private set; }

    // unit XZ heading the wind blows toward
    [JsonIgnore]
    public Vector2 DirectionVector
    {
        get
        {
            var r = Direction * MathF.PI / 180f;
            return new Vector2(MathF.Cos(r), MathF.Sin(r));
        }
    }

    // foliage actually moves only when enabled with non-zero strength and speed
    [JsonIgnore] public bool Animating => Enabled && Strength > 0f && Speed > 0f;

    public WindConfig() => OnDeserialized();

    public void OnDeserialized()
    {
        AuthoredStrength  = Strength;
        AuthoredSpeed     = Speed;
        AuthoredDirection = Direction;
    }
}

public sealed class CullingConfig : IJsonOnDeserialized
{
    [JsonPropertyName("cellSize")]       public float CellSize       { get; set; } = 16f;
    [JsonPropertyName("oversizeFactor")] public float OversizeFactor { get; set; } = 8f;

    [JsonIgnore] public float AuthoredCellSize       { get; private set; }
    [JsonIgnore] public float AuthoredOversizeFactor { get; private set; }

    public CullingConfig() => OnDeserialized();

    public void OnDeserialized()
    {
        AuthoredCellSize       = CellSize;
        AuthoredOversizeFactor = OversizeFactor;
    }
}