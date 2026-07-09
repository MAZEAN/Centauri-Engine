namespace Centauri.Loading;

using System.Text.Json;
using System.Text.Json.Serialization;

public class ComponentDefinition
{
    [JsonPropertyName("type")]    public string Type    { get; set; } = "";
    [JsonPropertyName("enabled")] public bool   Enabled { get; set; } = true;

    // every field other than "type"/"enabled" lands here — each component reads its own
    [JsonExtensionData] public Dictionary<string, JsonElement>? Params { get; set; }
}