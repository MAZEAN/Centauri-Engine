namespace Centauri.Loading;

using System.Text.Json.Serialization;

// A composable, optional list of entities. AppConfig's Render.EntitySetPaths lists zero or more
// of these to layer onto an environment at startup — by default that list is empty, so a fresh
// project boots into an empty (camera + skybox only) scene rather than always dragging in
// whatever demo/debug content happens to be lying around. Each file loaded this way keeps its
// own identity: EntitySetLoader.Save() writes each set's entities back to the file they came
// from, so composing several sets together doesn't collapse them into one.
public class EntitySetDefinition
{
    [JsonPropertyName("entities")] public List<EntityDefinition> Entities { get; set; } = [];
}
