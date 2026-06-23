namespace Centauri.Rendering.Helper;

using Graphics.Resources;
using World;

// Groups scene entities by shader, sorting each group by material so the renderer
// minimizes shader switches and texture binds. Rebuilt only when the scene changes
// (tracked by Scene.Revision).
public sealed class ShaderBatcher
{
    private readonly Dictionary<GLShader, List<Entity>> _groups = new();
    private int _revision = -1;

    public IReadOnlyDictionary<GLShader, List<Entity>> GetGroups(Scene scene)
    {
        if (scene.Revision == _revision)
            return _groups;

        _groups.Clear();

        foreach (var entity in scene.Entities)
        {
            if (entity.Material is not { } material)   // light-only / mesh-less entities
                continue;

            if (!_groups.TryGetValue(material.Shader, out var list))
            {
                list = new List<Entity>();
                _groups[material.Shader] = list;
            }

            list.Add(entity);
        }

        // sort each group by material so texture binds are minimized
        foreach (var list in _groups.Values)
            list.Sort((a, b) => a.Material!.SortKey.CompareTo(b.Material!.SortKey));

        _revision = scene.Revision;
        return _groups;
    }
}