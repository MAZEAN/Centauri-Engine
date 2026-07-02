namespace Centauri.Config;

using System.Text.Json.Serialization;
using Silk.NET.Input;

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
