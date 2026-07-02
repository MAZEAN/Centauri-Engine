namespace Centauri.Config;

using System.Text.Json.Serialization;

public sealed class TAAConfig : IJsonOnDeserialized
{
    [JsonPropertyName("enabled")]  public bool  Enabled  { get; set; } = false;
    [JsonPropertyName("feedback")] public float Feedback { get; set; } = 0.9f;

    [JsonIgnore] public float AuthoredFeedback { get; private set; }

    public TAAConfig() => OnDeserialized();

    public void OnDeserialized() => AuthoredFeedback = Feedback;
}
