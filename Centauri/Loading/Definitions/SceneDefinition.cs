namespace Centauri.Loading;

using System.Text.Json.Serialization;

public class SceneDefinition
{
    [JsonPropertyName("entities")] public List<EntityDefinition> Entities { get; set; } = [];
    [JsonPropertyName("cameras")]  public List<CameraDefinition> Cameras  { get; set; } = [];
    [JsonPropertyName("skybox")]   public List<SkyboxDefinition> Skyboxes { get; set; } = [];

    // Other scene files (relative to the project root, same as "model"/"material" paths)
    // whose entities/cameras/skybox get merged into this one at load — keeps a growing scene
    // as a thin index instead of one file holding everything. Optional; unused scenes are
    // unaffected.
    [JsonPropertyName("include")]  public List<string>?          Include  { get; set; }
}