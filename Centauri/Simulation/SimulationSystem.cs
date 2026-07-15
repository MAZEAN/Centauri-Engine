namespace Centauri.Simulation;

using World;
using Config;
using Physics;
using Rendering.Profiling;

// Advances scene state each frame. Component behaviour (animation, day/night) runs once per rendered
// frame on the real frame delta; rigid-body physics runs on a *fixed* timestep decoupled from the
// frame rate via an accumulator, then interpolates its results back for smooth rendering. The single
// hook point for gameplay / animation / physics.
public sealed class SimulationSystem : IDisposable
{
    private readonly AppConfig _config;

    // Lazily created the first frame physics is enabled, so a project that never turns physics on
    // pays nothing (no BEPU Simulation, no buffer pool) and behaves exactly as before.
    private PhysicsSystem? _physics;
    private float          _accumulator;

    // Per-frame physics stats for the Stats Overlay's "Physics" section (see PhysicsEngine.md).
    // Body counts read straight from _physics; steps/ms are frame-local (reset every Update call)
    // since a hitch can make one rendered frame cover several fixed steps, or — below timestepHz's
    // period — zero.
    public int   PhysicsDynamicBodies   => _physics?.DynamicBodyCount ?? 0;
    public int   PhysicsStaticBodies    => _physics?.StaticBodyCount  ?? 0;
    public int   PhysicsStepsThisFrame  { get; private set; }
    public float PhysicsStepMsThisFrame { get; private set; }

    public SimulationSystem(AppConfig config)
    {
        _config = config;
    }

    public void Update(Scene scene, float dt)
    {
        // Per-frame component logic stays on the real frame delta — these are visual animations, not
        // simulation, and don't need (or want) fixed-step determinism.
        TickComponents(scene, dt);

        if (_config.Physics.Enabled)
        {
            StepPhysics(scene, dt);
        }
        else
        {
            PhysicsStepsThisFrame  = 0;
            PhysicsStepMsThisFrame = 0f;
        }
    }

    private static void TickComponents(Scene scene, float dt)
    {
        var entities = scene.Entities;
        for (var i = 0; i < entities.Count; i++)
        {
            var entity = entities[i];
            if (entity.Enabled)
                entity.Update(dt);
        }
    }

    // Classic fixed-timestep accumulator (Gaffer's "Fix Your Timestep"): bank the frame's real time,
    // spend it in whole fixed steps, then interpolate the leftover so rendering never stutters or
    // ties simulation behaviour to frame rate.
    private void StepPhysics(Scene scene, float dt)
    {
        using var _ = Tracy.Scope("Physics");

        _physics ??= new PhysicsSystem(_config.Physics);
        _physics.Sync(scene);

        var fixedDt  = _config.Physics.FixedDelta;
        var maxSteps = _config.Physics.MaxStepsPerFrame;

        _accumulator += dt;

        var steps    = 0;
        var stepMs   = 0f;
        while (_accumulator >= fixedDt && steps < maxSteps)
        {
            _physics.StepFixed(fixedDt);
            stepMs       += _physics.LastStepMs;
            _accumulator -= fixedDt;
            steps++;
        }

        PhysicsStepsThisFrame  = steps;
        PhysicsStepMsThisFrame = stepMs;

        // If we bailed out on the step cap (a hitch), drop the unspent backlog rather than carry it —
        // and keep the interpolation fraction in [0,1) either way.
        if (_accumulator > fixedDt)
            _accumulator = fixedDt;

        _physics.Interpolate(_accumulator / fixedDt);
    }

    public void Dispose()
    {
        _physics?.Dispose();
        _physics = null;
    }
}
