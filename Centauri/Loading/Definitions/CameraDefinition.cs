namespace Centauri.Loading;

using System.Text.Json.Serialization;

public class CameraDefinition
{
    [JsonPropertyName("name")]     public string  Name     { get; set; } = "";
    [JsonPropertyName("position")] public float[] Position { get; set; } = [0f, 0f, 0f];
    [JsonPropertyName("up")]       public string  Up       { get; set; } = "Y";
    [JsonPropertyName("yaw")]      public float   Yaw      { get; set; }
    [JsonPropertyName("pitch")]    public float   Pitch    { get; set; }
    [JsonPropertyName("active")]   public bool Active { get; set; }
    [JsonPropertyName("primary")]  public bool Primary { get; set; }
}