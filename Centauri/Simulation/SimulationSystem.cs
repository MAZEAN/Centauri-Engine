namespace Centauri.Simulation;

using World;
using Config;

// Advances scene state each frame by ticking every enabled entity's components.
// The single hook point for gameplay / animation / (later) physics.
public sealed class SimulationSystem
{
    private readonly AppConfig _config;
    
    public SimulationSystem(AppConfig config)
    {
        _config = config;
    }
    
    public void Update(Scene scene, float dt)
    {
        var entities = scene.Entities;
        for (var i = 0; i < entities.Count; i++)
        {
            var entity = entities[i];
            if (entity.Enabled)
                entity.Update(dt);
        }
    }
}