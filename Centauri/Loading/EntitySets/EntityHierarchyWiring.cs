namespace Centauri.Loading;

using World;

// Resolves EntityDefinition.Parent references within one just-loaded file's entity list into
// live Transform.Parent links. Pure algorithm, no I/O and no dependency on EntitySetLoader's own
// tracking state — takes exactly the (def, entity) pairs built from one file and wires them.
internal static class EntityHierarchyWiring
{
    // Resolves each entity's "parent" (if any) against the other entities *from the same file*
    // only — cross-file parenting isn't supported, since load order between files isn't a
    // guaranteed topological order the way order within one file's own array is (EntitySetPaths
    // load in config order, but a later file's entities may load before an earlier file's, e.g.
    // via DefaultEntitySetPath). First match wins if names collide (names aren't required to be
    // unique elsewhere in this schema either). Doesn't touch Position/Scale/Rotation — those stay
    // exactly as authored, now interpreted as local-to-the-new-parent rather than local-to-world
    // (see EntityDefinition.Parent's own comment).
    public static void Wire(List<(EntityDefinition def, Entity entity)> built)
    {
        Dictionary<string, Entity>? byName = null;

        foreach (var (def, entity) in built)
        {
            if (string.IsNullOrEmpty(def.Parent)) continue;

            byName ??= BuildNameIndex(built);
            if (byName.TryGetValue(def.Parent, out var parent))
                entity.Transform.Parent = parent.Transform;
        }
    }

    private static Dictionary<string, Entity> BuildNameIndex(List<(EntityDefinition def, Entity entity)> built)
    {
        var byName = new Dictionary<string, Entity>();
        foreach (var (_, entity) in built)
            byName.TryAdd(entity.Name, entity);   // first match wins on a name collision
        return byName;
    }
}
