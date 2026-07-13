namespace Centauri.Loading;

using System.Text.Json.Serialization;

// The always-loaded half of a scene: cameras and skybox. Kept separate from entity content
// (EntitySetDefinition) so a project can have exactly one environment but zero or many
// interchangeable/optional entity sets loaded into it — see EntitySetLoader.
public class EnvironmentDefinition
{
    [JsonPropertyName("cameras")] public List<CameraDefinition> Cameras  { get; set; } = [];
    [JsonPropertyName("skybox")]  public List<SkyboxDefinition> Skyboxes { get; set; } = [];
}
