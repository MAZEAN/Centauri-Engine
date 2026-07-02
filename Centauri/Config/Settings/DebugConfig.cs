namespace Centauri.Config;

using System.Text.Json.Serialization;

public enum ShadingMode { Shaded, Normals, Depth, AmbientOcclusion, Velocity }

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
    [JsonPropertyName("showAnisotropicFilter")] public bool AnisotropicFilter { get; set; } = true;
    [JsonIgnore] public ShadingMode Shading { get; set; } = ShadingMode.Shaded;

    public void ToggleShowStatsOverlay()  => ShowStatsOverlay = !ShowStatsOverlay;
    public void CycleShading() =>
        Shading = (ShadingMode)(((int)Shading + 1) % ShadingModeCount);
}
