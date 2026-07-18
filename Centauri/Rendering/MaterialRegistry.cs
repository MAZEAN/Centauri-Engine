namespace Centauri.Rendering;

using System.Text.Json;
using System.Text.Json.Nodes;

using Loading;
using Utils.Misc;

// Indexes every .mat under Assets/Materials by id, resolves "extends" inheritance, and applies
// MaterialDefinition.Path texture prefixing — everything about turning a material id/path into
// a fully-resolved MaterialDefinition. Doesn't touch GL: ResourceSystem.LoadMaterial is what
// turns a resolved definition into an actual Material (Textures.Get/Shaders.Get), since that's
// the one step that needs the GL-backed asset caches this class has no reason to know about.
internal sealed class MaterialRegistry
{
    private readonly Dictionary<string, string> _paths;

    public MaterialRegistry() => _paths = Build();

    // Every material id this project's Assets/ knows about, for UI that lets you pick one to
    // place (the Outliner's "add entity" flow, the Inspector's per-slot material picker) rather
    // than typing a path/id by hand. Materials under a path segment starting with '_' (e.g.
    // Assets/Materials/_base/organic_pbr.mat) are excluded here — those are internal/template
    // materials meant only to be inherited from via "extends", not placed directly — but still
    // fully resolvable by id or "extends" (ResolvePath doesn't filter them), so Build still
    // indexes them same as everything else.
    public IEnumerable<string> Ids =>
        _paths.Where(kv => !IsHiddenAssetPath(kv.Value))
              .Select(kv => kv.Key)
              .OrderBy(k => k);

    // A literal path (contains '/') is used as-is — the escape hatch for anything not under
    // the registry, and how existing "Assets/Materials/X.mat"-style references keep working.
    // Anything else is looked up by id.
    public string ResolvePath(string idOrPath)
    {
        if (idOrPath.Contains('/'))
            return idOrPath;

        if (_paths.TryGetValue(idOrPath, out var path))
            return path;

        throw new Exception(
            $"Unknown material '{idOrPath}'. Available: {string.Join(", ", _paths.Keys.OrderBy(k => k))}");
    }

    public MaterialDefinition ReadDefinition(string idOrPath)
    {
        var def = ResolveJson(idOrPath, [])
            .Deserialize<MaterialDefinition>(JsonDefaults.Options)
            ?? throw new Exception($"Failed to deserialize material '{idOrPath}'.");

        ApplyTexturePathPrefix(def);
        return def;
    }

    // Resolves "extends" by merging the parent's JSON object with this one's at the raw node
    // level — fields the child doesn't mention simply aren't in its JsonObject, so they pass
    // through from the parent untouched, no need for every MaterialDefinition field to be
    // nullable just to distinguish "not set" from "explicitly set to the default value".
    private JsonObject ResolveJson(string idOrPath, HashSet<string> visiting)
    {
        var path = ResolvePath(idOrPath);
        if (!visiting.Add(path))
            throw new Exception($"Material inheritance cycle detected involving '{idOrPath}'.");

        var json = File.ReadAllText(PathResolver.Resolve(path));
        var node = JsonNode.Parse(json)?.AsObject()
            ?? throw new Exception($"Failed to parse material file: {path}");

        node.TryGetPropertyValue("extends", out var extendsNode);
        var parentId = extendsNode?.GetValue<string>();
        node.Remove("extends"); // strip regardless — never a real MaterialDefinition field

        if (string.IsNullOrEmpty(parentId))
            return node;

        var merged = ResolveJson(parentId, visiting).DeepClone()!.AsObject();
        foreach (var (key, value) in node)
            merged[key] = value?.DeepClone();

        return merged;
    }

    // Prefixes every bare-filename texture field with MaterialDefinition.Path — see that
    // field's own comment. Applied once, right after deserialization, so every consumer of a
    // MaterialDefinition's texture fields sees fully-resolved paths without needing to know
    // "path" exists.
    private static void ApplyTexturePathPrefix(MaterialDefinition def)
    {
        if (string.IsNullOrEmpty(def.Path)) return;

        var basePath = def.Path.EndsWith('/') ? def.Path : def.Path + "/";

        def.Albedo    = Prefix(def.Albedo);
        def.Normal    = Prefix(def.Normal);
        def.Roughness = Prefix(def.Roughness);
        def.Metallic  = Prefix(def.Metallic);
        def.AO        = Prefix(def.AO);
        def.Height    = Prefix(def.Height);
        def.Opacity   = Prefix(def.Opacity);
        return;

        string? Prefix(string? value) =>
            value is { Length: > 0 } && !value.Contains('/') ? basePath + value : value;
    }

    // Id defaults to the filename (sans extension); an explicit "id" field inside the file
    // overrides that. A literal path (anything containing '/') is still accepted as-is by
    // ResolvePath, so existing scene files work unchanged.
    private static Dictionary<string, string> Build()
    {
        var registry = new Dictionary<string, string>();
        var root = PathResolver.Resolve(".");
        var materialsDir = PathResolver.Resolve("Assets/Materials");

        if (!Directory.Exists(materialsDir))
            return registry;

        foreach (var file in Directory.EnumerateFiles(materialsDir, "*.mat", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            var id = Path.GetFileNameWithoutExtension(file);

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                if (doc.RootElement.TryGetProperty("id", out var idProp) && idProp.GetString() is { Length: > 0 } explicitId)
                    id = explicitId;
            }
            catch (JsonException)
            {
                // malformed JSON — surfaces as a clear error the moment this file is actually
                // loaded (ReadDefinition), no need to duplicate that here
            }

            if (registry.TryGetValue(id, out var existing))
                throw new Exception($"Duplicate material id '{id}': '{existing}' and '{relative}'.");

            registry[id] = relative;
        }

        return registry;
    }

    private static bool IsHiddenAssetPath(string relativePath) =>
        relativePath.Split('/').Any(segment => segment.StartsWith('_'));
}
