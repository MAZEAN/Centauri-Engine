namespace Centauri.Config;

using System.Text.Json.Serialization;

public class AppConfig
{
    [JsonPropertyName("window")]  public WindowConfig Window { get; init; } = new();
    [JsonPropertyName("camera")]  public CameraConfig Camera { get; init; } = new();
    [JsonPropertyName("render")]  public RenderConfig Render { get; init; } = new();
    [JsonPropertyName("imGui")]   public ImGuiConfig  ImGui { get; init; } = new();
    [JsonPropertyName("input")]   public InputConfig  Input { get; init; } = new();
    [JsonPropertyName("debug")]   public DebugConfig  Debug { get; init; } = new();
    [JsonPropertyName("ibl")]     public IBLConfig    IBL { get; init; } = new();
    [JsonPropertyName("reflectionProbe")] public ReflectionProbeConfig ReflectionProbe { get; init; } = new();
    [JsonPropertyName("planarReflection")] public PlanarReflectionConfig PlanarReflection { get; init; } = new();
    [JsonPropertyName("shadows")] public ShadowConfig Shadows { get; init; } = new();
    [JsonPropertyName("grading")] public ColorGrading ColorGrading { get; init; } = new();
    [JsonPropertyName("ssao")]    public SSAOConfig   SSAO { get; init; } = new();
    [JsonPropertyName("bloom")]   public BloomConfig  Bloom { get; init; } = new();
    [JsonPropertyName("autoExposure")] public AutoExposureConfig AutoExposure { get; init; } = new();
    [JsonPropertyName("ssr")]     public SSRConfig    SSR { get; init; } = new();
    [JsonPropertyName("taa")]     public TAAConfig    TAA { get; init; } = new();
    [JsonPropertyName("wind")]    public WindConfig   Wind { get; init; } = new();
    [JsonPropertyName("culling")] public CullingConfig Culling { get; init; } = new();
    [JsonPropertyName("sky")]     public SkyConfig    Sky { get; init; } = new();
}