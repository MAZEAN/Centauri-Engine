namespace Centauri.Config;

using System.Text.Json.Serialization;
using Silk.NET.Windowing;

public class WindowConfig
{
    [JsonPropertyName("title")]       public string Title { get; init; } = "Centauri";
    [JsonPropertyName("state")]       public WindowState State { get; init; } = WindowState.Maximized;
    [JsonPropertyName("border")]      public WindowBorder Border { get; init; } = WindowBorder.Hidden;
    [JsonPropertyName("enableVSync")] public bool EnableVSync { get; init; } = true;
    [JsonPropertyName("samples")]     public int Samples { get; init; } = 4;
    [JsonPropertyName("clearColor")]  public float[] ClearColor { get; init; } = [1.0f, 1.0f, 1.0f, 1.0f];
}
