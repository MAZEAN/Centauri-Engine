namespace Centauri.Config;

using System.Numerics;
using System.Text.Json.Serialization;

// Rigid-body physics (BEPUphysics2). Optional and off by default: with Enabled = false the
// SimulationSystem never even constructs a BEPU Simulation, so the engine runs exactly as before.
// See Docs/Documentation/PhysicsEngine.md.
public sealed class PhysicsConfig
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = false;

    // World gravity in m/s². Y-down to match the engine's world axes.
    [JsonPropertyName("gravity")] public float[] Gravity { get; set; } = [0f, -9.81f, 0f];

    // Simulation rate. The fixed step the accumulator advances in is 1 / TimestepHz — independent
    // of the render frame rate, so simulation stays deterministic and stable regardless of FPS.
    [JsonPropertyName("timestepHz")] public float TimestepHz { get; set; } = 60f;

    // BEPU solver tuning. Substeps trade cost for stiffness/stacking stability; velocity iterations
    // refine each substep. 8/1 is BEPU's general-purpose default.
    [JsonPropertyName("solverVelocityIterations")] public int SolverVelocityIterations { get; set; } = 8;
    [JsonPropertyName("solverSubsteps")]           public int SolverSubsteps           { get; set; } = 1;

    // Upper bound on fixed steps executed in a single frame. Caps the "spiral of death": after a
    // hitch (breakpoint, GC, window drag) the leftover simulation time is dropped rather than
    // chased with an unbounded catch-up burst that would only cause the next hitch.
    [JsonPropertyName("maxStepsPerFrame")] public int MaxStepsPerFrame { get; set; } = 8;

    [JsonIgnore] public Vector3 GravityVector =>
        new(Gravity.Length > 0 ? Gravity[0] : 0f,
            Gravity.Length > 1 ? Gravity[1] : 0f,
            Gravity.Length > 2 ? Gravity[2] : 0f);

    // Guarded so a zero/negative config value can't divide by zero or stall the accumulator.
    [JsonIgnore] public float FixedDelta => 1f / MathF.Max(1f, TimestepHz);
}
