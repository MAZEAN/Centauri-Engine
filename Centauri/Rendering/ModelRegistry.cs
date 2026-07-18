namespace Centauri.Rendering;

using System.Text.Json;

using Loading;
using Utils.Misc;

// Indexes every .model under Assets/Objects by id, the same way MaterialRegistry indexes
// materials — "model": "Tree" instead of "Assets/Objects/Trees/Tree.glb", plus (unlike a .mat,
// which *is* the resource) each .model file points at the actual geometry file via "path" and
// can carry reusable defaults (default material binding, default triplanar override) for every
// entity that places it, so a repeated placement doesn't need to repeat them.
internal sealed class ModelRegistry
{
    private readonly Dictionary<string, string> _paths;
    private readonly Dictionary<string, ModelDefinition> _definitions;

    public ModelRegistry() => (_paths, _definitions) = Build();

    public IEnumerable<string> Ids => _paths.Keys.OrderBy(k => k);

    // Same convention as MaterialRegistry.ResolvePath: a literal path is used as-is, anything
    // else is looked up by id.
    public string ResolvePath(string idOrPath)
    {
        if (idOrPath.Contains('/'))
            return idOrPath;

        if (_paths.TryGetValue(idOrPath, out var path))
            return path;

        throw new Exception(
            $"Unknown model '{idOrPath}'. Available: {string.Join(", ", _paths.Keys.OrderBy(k => k))}");
    }

    // Null for a literal path or an id with no .model file (nothing to default from) — callers
    // fall back to whatever the entity itself specifies.
    public ModelDefinition? GetDefinition(string idOrPath) =>
        !idOrPath.Contains('/') && _definitions.TryGetValue(idOrPath, out var def) ? def : null;

    private static (Dictionary<string, string>, Dictionary<string, ModelDefinition>) Build()
    {
        var registry    = new Dictionary<string, string>();
        var definitions = new Dictionary<string, ModelDefinition>();
        var objectsDir  = PathResolver.Resolve("Assets/Objects");

        if (!Directory.Exists(objectsDir))
            return (registry, definitions);

        foreach (var file in Directory.EnumerateFiles(objectsDir, "*.model", SearchOption.AllDirectories))
        {
            ModelDefinition def;
            try
            {
                def = JsonSerializer.Deserialize<ModelDefinition>(File.ReadAllText(file), JsonDefaults.Options)
                      ?? throw new Exception($"Failed to deserialize model file: {file}");
            }
            catch (JsonException e)
            {
                throw new Exception($"Malformed model file '{file}': {e.Message}", e);
            }

            if (string.IsNullOrEmpty(def.Path))
                throw new Exception($"Model file '{file}' is missing its required \"path\".");

            var id = string.IsNullOrEmpty(def.Id) ? Path.GetFileNameWithoutExtension(file) : def.Id;

            if (registry.TryGetValue(id, out var existing))
                throw new Exception($"Duplicate model id '{id}': '{existing}' and '{def.Path}'.");

            registry[id]    = def.Path;
            definitions[id] = def;
        }

        return (registry, definitions);
    }
}
