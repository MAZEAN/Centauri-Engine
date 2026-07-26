namespace Centauri.Loading;

using System.Text.Json;

using Config;
using Rendering;
using Utils.Misc;
using World;
using Simulation.Physics;

// Loads zero or more EntitySetDefinition files (AppConfig's Render.EntitySetPaths, plus
// Render.DefaultEntitySetPath if it exists — see EffectivePaths) into the scene, and can write
// them back out. Each file keeps its own identity end to end: every live Entity's tracking entry
// (TrackedEntitySet) remembers both the EntityDefinition it was built from and which file that
// came from, so Save() always has an unambiguous, correct destination for it — including
// entities added at runtime via CreateEntity(), which are attributed to
// Render.DefaultEntitySetPath until saved once, at which point that file starts existing on disk
// like any other set and (from then on) loads automatically too, without needing to be added to
// EntitySetPaths by hand.
public class EntitySetLoader
{
    private readonly ResourceSystem _resourceSystem;
    private readonly Scene _scene;
    private readonly AppConfig _config;
    private readonly EntityFactory _factory;
    private readonly TrackedEntitySet _tracked = new();

    public EntitySetLoader(ResourceSystem resourceSystem, Scene scene, AppConfig config)
    {
        _resourceSystem = resourceSystem;
        _scene = scene;
        _config = config;
        _factory = new EntityFactory(resourceSystem);
    }

    // Loads every configured entity set (Render.EntitySetPaths), in order, plus
    // Render.DefaultEntitySetPath if it exists on disk and isn't already one of them — entities
    // created live (CreateEntity) save there, and without this they'd be written correctly but
    // never loaded back, since nothing added that path to EntitySetPaths for them. An empty
    // effective list is valid and expected — the default is an empty scene (environment only);
    // entity content is opt-in via config, or added live via CreateEntity().
    public void LoadAll()
    {
        var paths = EffectivePaths();
        if (paths.Count == 0) return;

        var definitions = paths.Select(p => (path: p, def: LoadDefinition(p))).ToList();

        _resourceSystem.PreloadEntities(definitions.SelectMany(d => d.def.Entities));

        foreach (var (path, def) in definitions)
        {
            _tracked.MarkFileKnown(path);

            // Two passes: every entity in the file must exist before any "parent" reference can
            // resolve, since a child is free to appear earlier in the file than its parent (JSON
            // array order isn't required to be a topological order) — see EntityHierarchyWiring.
            var built = new List<(EntityDefinition def, Entity entity)>(def.Entities.Count);
            foreach (var e in def.Entities)
                built.Add((e, AddFromDefinition(e, path)));

            EntityHierarchyWiring.Wire(built);
        }
    }

    // Discards every entity *this loader* is responsible for and reloads from disk — an easy way
    // back to the last saved state (or the original authored one, if nothing's been saved yet)
    // when live edits went somewhere you didn't want. Only removes tracked entities, not
    // Scene.Entities wholesale — the environment's own entities (e.g. its "sun", added directly
    // by EnvironmentLoader) aren't ours to touch and must survive a reset. Re-derives
    // EffectivePaths from scratch rather than reusing the tracked file list, so a file that only
    // just started existing on disk (DefaultEntitySetPath, written by a Save() earlier this
    // session) is picked up too.
    public void Reset()
    {
        foreach (var entity in _tracked.Entities.ToList())
        {
            _scene.RemoveEntity(entity);
            entity.Dispose();
        }

        _tracked.Clear();
        LoadAll();
    }

    private List<string> EffectivePaths()
    {
        var paths = new List<string>(_config.Render.EntitySetPaths);

        var defaultPath = _config.Render.DefaultEntitySetPath;
        if (!paths.Contains(defaultPath) && File.Exists(PathResolver.Resolve(defaultPath)))
            paths.Add(defaultPath);

        return paths;
    }

    private static EntitySetDefinition LoadDefinition(string path)
    {
        var fullPath = PathResolver.Resolve(path);
        var json = File.ReadAllText(fullPath);
        return JsonSerializer.Deserialize<EntitySetDefinition>(json, JsonDefaults.Options)
               ?? throw new Exception($"Failed to deserialize entity set file: {path}");
    }

    private Entity AddFromDefinition(EntityDefinition e, string sourcePath)
    {
        var entity = _factory.Build(e);

        _scene.AddEntity(entity);
        _tracked.Track(entity, e, sourcePath);

        return entity;
    }

    // Adds a brand-new entity (no prior EntityDefinition) placing the given model id — the
    // "compose a new entity from the available object list" workflow. materialId is optional;
    // when omitted the usual resolution chain applies (the model's own default binding, else
    // DefaultMaterial). Attributed to Render.DefaultEntitySetPath so Save() has somewhere to put
    // it; that file doesn't need to already exist.
    public Entity CreateEntity(string? modelId, string? materialId = null, string name = "New Entity")
    {
        var def = new EntityDefinition
        {
            Name     = name,
            Model    = modelId,
            Material = materialId,
        };

        var entity = _factory.Build(def);

        _scene.AddEntity(entity);
        _tracked.Track(entity, def, _config.Render.DefaultEntitySetPath);

        return entity;
    }

    // Live re-parent from the Inspector's Hierarchy section. parent = null moves the entity to
    // the scene root. Transform.Parent's own cycle guard (assigning an entity as its own
    // descendant's ancestor) throws — caught here and reported as a no-op rather than propagating
    // into the render loop, since the inspector can't easily pre-validate every combo selection
    // against every other entity's current subtree before the user picks it. Position/Scale/
    // Rotation are left exactly as they are — no world-position-preserving compensation, same as
    // the load-time wiring (see EntityDefinition.Parent's comment); the entity visibly jumps if
    // its local transform wasn't already authored relative to the new parent. Doesn't write
    // source.Parent directly — ToDefinition re-derives it live from entity.Transform.Parent at
    // Save() time instead of trusting a cached name here, since the live Transform graph is the
    // actual source of truth (and could in principle be changed by something other than this
    // method — a cached copy would just be one more thing that could drift out of sync).
    public bool SetParent(Entity entity, Entity? parent)
    {
        try
        {
            entity.Transform.Parent = parent?.Transform;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;   // would create a cycle — Transform.Parent already refused it
        }
    }

    // Reassigns exactly one mesh slot's material asset, leaving every other slot exactly as it
    // was — the entity-level equivalent of EditMaterial's per-slot scalar edits below, but for
    // swapping the *asset* itself rather than tweaking a property on the currently-assigned one.
    // Rebuilds the entity's full per-slot id list (GetMaterialIdsPerSlot) so the other slots'
    // *bindings* survive the round-trip through EntityDefinition even though only one changed;
    // collapses back down to the compact singular "material" field when every slot ends up
    // wanting the same id, so a simple single-material entity's saved JSON doesn't grow a
    // needless full-length "materials" array just because one of its (identical) slots got
    // re-picked.
    public void SetMaterialSlot(Entity entity, int slotIndex, string materialId)
    {
        if ((uint)slotIndex >= (uint)entity.Materials.Count) return;

        entity.SetMaterial(slotIndex, _resourceSystem.GetMaterial(materialId));

        // ShaderBatcher.GetBatches caches its groupings by Scene.Revision — without this, a
        // swapped material never actually renders: the entity is still drawn against whichever
        // Batch it was grouped into before the swap, and DrawMesh shades from that Batch's own
        // captured Materials snapshot, not from re-reading entity.Materials each frame. Scalar
        // property edits (EditMaterial in the inspector) don't need this because
        // MakeMaterialUnique already calls it once, the first time a shared material is cloned —
        // after that, further edits mutate the same (now-unique) instance in place, which the
        // stale Batch snapshot already points at too. A full reference swap to a *different*
        // Material has no such shared instance to fall back on, so every call needs its own
        // rebuild.
        _scene.MarkDirty();

        if (!_tracked.TryGetSource(entity, out var source)) return;

        var ids = GetMaterialIdsPerSlot(entity);
        // A slot with no resolvable id at all (no binding anywhere in the chain — see
        // GetMaterialIdsPerSlot) can't be represented in the schema's "materials" array, which
        // only holds real ids; falling back to the id just picked is the least surprising choice
        // (that slot was rendering the engine's flat-white DefaultMaterial before this edit
        // anyway, so "changing" it here isn't a real content loss).
        ids[slotIndex] = materialId;
        for (var i = 0; i < ids.Length; i++)
            ids[i] ??= materialId;

        if (ids.Distinct().Count() == 1)
        {
            source.Material  = ids[0];
            source.Materials = null;
        }
        else
        {
            source.Material  = null;
            source.Materials = new MaterialBinding { Indexed = ids! };
        }
    }

    // The material id currently authored for each of the entity's mesh slots, mirroring
    // EntityFactory.ResolveMaterials's own binding-priority chain exactly (entity's own binding,
    // else its singular "material", else the placed model's own default binding) but returning
    // ids instead of resolved Material objects — used both to seed the inspector's per-slot
    // combos and, in SetMaterialSlot above, to carry every *other* slot's current id forward into
    // a freshly-rebuilt binding. A slot is null when nothing in the chain resolves it (falls back
    // to ResourceSystem.DefaultMaterial at load time, which has no id of its own). Untracked
    // entities (e.g. the environment's own "sun") return an all-null array.
    public string?[] GetMaterialIdsPerSlot(Entity entity)
    {
        var count = entity.Materials.Count;
        var ids = new string?[count];
        if (!_tracked.TryGetSource(entity, out var source)) return ids;

        var modelDef = !string.IsNullOrEmpty(source.Model) ? _resourceSystem.GetModelDefinition(source.Model) : null;
        var binding = source.Materials
                      ?? (!string.IsNullOrEmpty(source.Material) ? new MaterialBinding { Indexed = [source.Material] } : null)
                      ?? modelDef?.Materials;

        for (var i = 0; i < count; i++)
        {
            var meshName = entity.Model?.Meshes[i].Name;
            ids[i] = binding?.Named is { } named && !string.IsNullOrEmpty(meshName) && named.TryGetValue(meshName, out var byName)
                ? byName
                : binding?.Indexed is { Length: > 0 } indexed
                    ? indexed[Math.Min(i, indexed.Length - 1)]
                    : null;
        }

        return ids;
    }

    // Keeps the tracked EntityDefinition's Components list in sync with a live RigidBody edit
    // (inspector "Physics" section: attach, detach, or change Kind/Shape/Mass) — the same
    // "mirror the live edit back into the tracked source" pattern SetMaterial uses, so Save()
    // (which just re-emits source.Components verbatim) persists it without needing its own
    // physics-specific write path. rb = null removes any existing "rigidBody" entry (detach);
    // untracked entities (e.g. the environment's own "sun") are a no-op, same as SetMaterial.
    public void SyncRigidBodyDefinition(Entity entity, RigidBody? rb)
    {
        if (!_tracked.TryGetSource(entity, out var source)) return;

        var components = source.Components ??= new List<ComponentDefinition>();
        var existing = components.FirstOrDefault(
            c => c.Type.Equals("rigidBody", StringComparison.OrdinalIgnoreCase));

        if (rb is null)
        {
            if (existing is not null) components.Remove(existing);
            return;
        }

        var def = existing ?? new ComponentDefinition { Type = "rigidBody" };
        def.Enabled = rb.Enabled;
        def.Params = new Dictionary<string, JsonElement>
        {
            ["kind"]  = JsonSerializer.SerializeToElement(rb.Kind  == BodyKind.Static  ? "static"  : "dynamic"),
            ["shape"] = JsonSerializer.SerializeToElement(rb.Shape == BodyShape.Sphere ? "sphere"  : "box"),
            ["mass"]  = JsonSerializer.SerializeToElement(rb.Mass),
        };

        if (existing is null) components.Add(def);
    }

    // Removes an entity the editor created/loaded — drops its save tracking too, so a deleted
    // entity doesn't reappear on the next Save() of whichever file it belonged to. Any children
    // are promoted to the scene root first (Transform.Parent = null) rather than cascade-deleted
    // or left pointing at a disposed entity's Transform — Entity.Dispose() doesn't clear
    // Transform, so a still-linked child would keep computing a valid (just orphaned-from-the-
    // scene) WorldMatrix through it, which is more surprising than an explicit unparent.
    public void DeleteEntity(Entity entity)
    {
        foreach (var child in entity.Transform.Children.ToList())
            child.Parent = null;

        _scene.RemoveEntity(entity);
        entity.Dispose();
        _tracked.Untrack(entity);
    }

    // Captures enough to reconstruct this entity later via Restore (Editing.Undo.
    // DeleteEntityCommand) — the same EntityDefinition snapshot Save() would write for it right
    // now, plus which file it's tracked under. Untracked entities (e.g. the environment's own
    // "sun") return null; DeleteEntity's only caller (InputSystem's Delete-key handler) already
    // gates deletion behind Edit mode + a live selection, but an untracked entity was never
    // deletable through that path to begin with (DeleteEntity itself doesn't check tracking, so
    // this guard exists for Capture specifically, not to duplicate that gate).
    public (EntityDefinition Definition, string SourcePath)? Capture(Entity entity)
    {
        var sourcePath = _tracked.FileOf(entity);
        if (sourcePath is null) 
            return null;

        return (ToDefinition(entity), sourcePath);
    }

    // The Undo counterpart to DeleteEntity — re-inserts an entity from a previously-captured
    // EntityDefinition (see Capture above) and re-parents it if the definition names a parent
    // that's still present in the scene (first name match wins, same tiebreak
    // EntityHierarchyWiring uses at load time). Doesn't attempt to restore any children the
    // entity had *before* its deletion — DeleteEntity already promoted them to the scene root as
    // a permanent side effect at delete time, and re-linking that here is out of scope for this
    // first, coarse undo pass (see Docs/Documentation/Undo.md).
    public Entity Restore(EntityDefinition definition, string sourcePath)
    {
        var entity = AddFromDefinition(definition, sourcePath);

        if (!string.IsNullOrEmpty(definition.Parent))
        {
            var parent = _scene.Entities.FirstOrDefault(e => e.Name == definition.Parent);
            if (parent is not null)
                entity.Transform.Parent = parent.Transform;
        }

        return entity;
    }

    // Writes every known file back out (grouping live entities by file), one EntitySetDefinition
    // per file — composing several sets together at load time never collapses them into one on
    // save. Iterates the tracked file list rather than deriving it from _scene.Entities, so
    // deleting the *last* entity a file had still rewrites it as empty instead of leaving its
    // stale on-disk content untouched (nothing would otherwise map to that file anymore).
    public void Save()
    {
        var byFile = _scene.Entities
            .Where(e => _tracked.FileOf(e) is not null)
            .ToLookup(e => _tracked.FileOf(e));

        foreach (var file in _tracked.KnownFiles)
        {
            var outDef = new EntitySetDefinition { Entities = byFile[file].Select(ToDefinition).ToList() };
            var json = JsonSerializer.Serialize(outDef, JsonDefaults.Options);
            File.WriteAllText(PathResolver.Resolve(file), json);
        }
    }

    private EntityDefinition ToDefinition(Entity entity)
    {
        _tracked.TryGetSource(entity, out var source);
        var parentName = entity.Transform.Parent is { } p ? _tracked.FindOwner(p)?.Name : null;
        
        return EntityDefinitionWriter.Write(entity, source, parentName);
    }
}
