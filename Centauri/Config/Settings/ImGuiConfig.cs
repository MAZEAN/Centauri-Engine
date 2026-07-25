namespace Centauri.Config;

using System.Text.Json.Serialization;

public class ImGuiConfig
{
    [JsonPropertyName("font")] public string Font { get; init; } = "Assets/Fonts/IosevkaCharon-Regular.ttf";
    [JsonPropertyName("fontSize")] public float FontSize { get; init; } = 20f;

    // Per-panel background opacity (PanelHost.Place's bgAlpha), editable live from
    // PanelAppearanceSection — see EditorLayout.md. Runtime-only like the rest of Debug's toggles
    // (not written back to config.json on change); defaults match the fully-opaque look every
    // panel had before this was configurable.
    [JsonPropertyName("topBarAlpha")]      public float TopBarAlpha      { get; set; } = 1f;
    [JsonPropertyName("leftToolsAlpha")]   public float LeftToolsAlpha   { get; set; } = 1f;
    [JsonPropertyName("outlinerAlpha")]    public float OutlinerAlpha    { get; set; } = 1f;
    [JsonPropertyName("propertiesAlpha")]  public float PropertiesAlpha  { get; set; } = 1f;
    [JsonPropertyName("statsAlpha")]       public float StatsAlpha       { get; set; } = 1f;
    [JsonPropertyName("performanceAlpha")] public float PerformanceAlpha { get; set; } = 1f;
}
