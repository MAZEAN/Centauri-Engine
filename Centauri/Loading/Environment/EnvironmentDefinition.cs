namespace Centauri.Loading;

using System.Text.Json.Serialization;

// The always-loaded half of a scene: cameras and skybox. Kept separate from entity content
// (EntitySetDefinition) so a project can have exactly one environment but zero or many
// interchangeable/optional entity sets loaded into it — see EntitySetLoader.
public class EnvironmentDefinition
{
    [JsonPropertyName("cameras")] public List<CameraDefinition> Cameras  { get; set; } = [];
    [JsonPropertyName("skybox")]  public List<SkyboxDefinition> Skyboxes { get; set; } = [];

    // Optional directional light driving procedural sky/clouds/IBL and (via a "dayNight"
    // component, same schema entity sets use) the day/night cycle — all three currently require
    // an actual DirectionalLight entity to read a sun direction from (see
    // RenderingSystem.UpdateProceduralIbl), so with entity sets empty by default this is the
    // only way those systems have anything to work with out of the box. Reuses EntityDefinition
    // wholesale (name/light/components) rather than a parallel light-only schema — it's added to
    // the scene exactly like any other entity, just sourced from the environment instead of an
    // entity set, and (like the rest of the environment) isn't affected by EntitySetLoader.Save().
    [JsonPropertyName("sun")] public EntityDefinition? Sun { get; set; }
}
