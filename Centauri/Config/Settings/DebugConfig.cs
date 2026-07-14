namespace Centauri.Config;

using System.Text.Json.Serialization;

// ParallaxDebug doesn't come from a G-buffer like the others (see BufferDebugView) — it's
// produced directly by shaderPBR.frag's own uDebugParallax branch on every material with a
// bound height map, so RenderingSystem/BufferDebugView both special-case it: no prepass forced,
// nothing to overlay after the fact. See ShaderUniformBinder.UploadMaterial.
public enum ShadingMode { Shaded, Normals, Depth, AmbientOcclusion, Velocity, ParallaxDebug }

public class DebugConfig
{
    private static readonly int ShadingModeCount = Enum.GetValues<ShadingMode>().Length;

    [JsonPropertyName("enableCulling")]     public bool EnableCulling     { get; set; } = true;
    [JsonPropertyName("showDebugView")]     public bool ShowDebugView     { get; set; } = false;
    [JsonPropertyName("showBoundingBoxes")] public bool ShowBoundingBoxes { get; set; } = false;
    [JsonPropertyName("showFrustums")]      public bool ShowFrustums      { get; set; } = false;
    [JsonPropertyName("showCameras")]       public bool ShowCameras       { get; set; } = false;
    [JsonPropertyName("showGrid")]          public bool ShowGrid          { get; set; } = false;
    [JsonPropertyName("showCullingGrid")]   public bool ShowCullingGrid   { get; set; } = false;
    [JsonPropertyName("showStatsOverlay")]  public bool ShowStatsOverlay  { get; set; } = true;
    [JsonPropertyName("showSkybox")]        public bool ShowSkybox        { get; set; } = true;
    [JsonPropertyName("showGPUTimings")]    public bool ShowGPUTimings    { get; set; } = true;
    [JsonPropertyName("tracyEnabled")]      public bool TracyEnabled      { get; set; } = false;
    [JsonPropertyName("showAnisotropicFilter")] public bool AnisotropicFilter { get; set; } = true;
    // Diagnostic: fall back to Forward doing its own fresh depth test/write (DepthFunc(Less),
    // DepthMask(true)) instead of reusing ZPrepass's depth via Lequal. Lets a suspected
    // depth-reuse bug be A/B tested with a single toggle instead of a code round-trip.
    [JsonPropertyName("enableZPrepass")]    public bool EnableZPrepass    { get; set; } = true;
    // Dev convenience, off by default: watches Shaders/ and hot-swaps a shader's live GL program
    // in place on save instead of requiring a restart — see Utils.Misc.ShaderHotReload. Read
    // once at startup (Engine.InitializeSystems), not live-reactive itself — restart to pick up
    // a change to this specific flag.
    [JsonPropertyName("shaderHotReload")]   public bool ShaderHotReload   { get; set; } = false;

    [JsonIgnore] public ShadingMode Shading { get; set; } = ShadingMode.Shaded;

    public void ToggleShowStatsOverlay()  => ShowStatsOverlay = !ShowStatsOverlay;
    public void CycleShading() =>
        Shading = (ShadingMode)(((int)Shading + 1) % ShadingModeCount);
}
