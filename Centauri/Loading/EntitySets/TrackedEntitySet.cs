namespace Centauri.Loading;

using World;

// The bookkeeping EntitySetLoader needs to answer "where did this live Entity come from, and
// where does it get saved back to" — bundles what used to be three separate parallel
// dictionaries (_sources / _fileOf / _knownFiles) behind one cohesive surface. An entity here
// always has both its originating EntityDefinition and its source file tracked together; the
// environment's own entities (e.g. its "sun", added directly by EnvironmentLoader) are never
// tracked here, which is what makes them immune to Reset()/Save() and every other operation
// below that only touches tracked entities.
internal sealed class TrackedEntitySet
{
    private readonly Dictionary<Entity, EntityDefinition> _sources = new();
    private readonly Dictionary<Entity, string> _fileOf = new();

    // Every file ever loaded (or written to via CreateEntity's DefaultEntitySetPath) this
    // session, independent of whether it currently has any live entities — Save() needs this so
    // deleting the *last* entity that came from a file still rewrites that file (as now-empty),
    // instead of silently leaving its stale on-disk content untouched because nothing tracked
    // maps to it anymore.
    private readonly HashSet<string> _knownFiles = new();

    public IReadOnlyCollection<Entity> Entities   => _sources.Keys;
    public IReadOnlyCollection<string> KnownFiles => _knownFiles;

    public void Track(Entity entity, EntityDefinition source, string file)
    {
        _sources[entity] = source;
        _fileOf[entity]  = file;
        _knownFiles.Add(file);
    }

    public void Untrack(Entity entity)
    {
        _sources.Remove(entity);
        _fileOf.Remove(entity);
    }

    public bool TryGetSource(Entity entity, out EntityDefinition source) =>
        _sources.TryGetValue(entity, out source!);

    public string? FileOf(Entity entity) => _fileOf.TryGetValue(entity, out var f) ? f : null;

    public void MarkFileKnown(string file) => _knownFiles.Add(file);

    public void Clear()
    {
        _sources.Clear();
        _fileOf.Clear();
        _knownFiles.Clear();
    }

    // Reverse lookup: given a Transform known to be some tracked entity's Parent, find which
    // entity that is, so it can be written back out by name (the schema has no other stable
    // handle — see EntityDefinition.Parent). O(n) over tracked entities; fine at this engine's
    // scene scale (same tradeoff Scene.Pick/FindComponent already make). Null if the parent is
    // untracked (e.g. the environment's own "sun" — parenting to it isn't supported, same as
    // every other untracked-entity edge case in this loader).
    public Entity? FindOwner(Transform transform)
    {
        foreach (var candidate in _sources.Keys)
            if (ReferenceEquals(candidate.Transform, transform))
                return candidate;
        return null;
    }
}
