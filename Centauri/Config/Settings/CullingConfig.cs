namespace Centauri.Config;

using System.Text.Json.Serialization;

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
