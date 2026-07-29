namespace Centauri.UI.Panels.Inspector.Sections;

using ImGuiNET;

using World;
using Common;
using Loading;
using Simulation.Physics;
using Editing.Undo;

// Attaches/edits/detaches a RigidBody on the selected entity. Shape is derived from the
// model's own bounds (see PhysicsSystem.Register) — there's nothing to author there beyond
// Box/Sphere. Any edit after the initial attach calls RigidBody.MarkDirty() so PhysicsSystem
// tears down and recreates the BEPU body on its next Sync instead of silently keeping the old
// one; SyncRigidBodyDefinition mirrors the same edit into the entity's saved definition so it
// round-trips (see EntitySetLoader.Save — it just re-emits source.Components verbatim).
internal sealed class EntityPhysicsSection
{
    private static readonly string[] PhysicsKinds  = ["None", "Dynamic", "Static"];
    private static readonly string[] PhysicsShapes = ["Box", "Sphere"];

    private readonly EntitySetLoader _entitySetLoader;

    public EntityPhysicsSection(EntitySetLoader entitySetLoader) => _entitySetLoader = entitySetLoader;

    public void Draw(Entity e, CommandHistory? undo)
    {
        using var s = Widgets.Section("Physics");
        if (!s.Open) return;

        var rb = e.GetComponent<RigidBody>();
        var kindIndex = rb switch
        {
            null                      => 0,
            { Kind: BodyKind.Static } => 2,
            _                         => 1
        };

        if (Widgets.ComboRow("Body", ref kindIndex, PhysicsKinds))
        {
            // Kind/Shape edits attach, detach, or rebuild the component itself — not a plain value
            // swap the generic Widgets undo mechanism can express — so RigidBodyCommand captures
            // the whole before/after RigidBodyState (or null for "no RigidBody") and replays the
            // same MarkDirty()/SyncRigidBodyDefinition() side effects on both Undo and Redo.
            var before = rb is null ? (RigidBodyState?)null : RigidBodyState.Of(rb);

            if (kindIndex == 0)
            {
                if (rb is not null)
                {
                    e.RemoveComponent<RigidBody>();
                    _entitySetLoader.SyncRigidBodyDefinition(e, null);
                    undo?.Push(new RigidBodyCommand(e, _entitySetLoader, before, null));
                }
                return;
            }

            var kind = kindIndex == 2 ? BodyKind.Static : BodyKind.Dynamic;
            if (rb is null)
            {
                rb = e.AddComponent(new RigidBody { Kind = kind });
            }
            else
            {
                rb.Kind = kind;
                rb.MarkDirty();
            }
            _entitySetLoader.SyncRigidBodyDefinition(e, rb);
            undo?.Push(new RigidBodyCommand(e, _entitySetLoader, before, RigidBodyState.Of(rb)));
        }

        if (rb is null) return;

        var shapeIndex = rb.Shape == BodyShape.Sphere ? 1 : 0;
        if (Widgets.ComboRow("Shape", ref shapeIndex, PhysicsShapes))
        {
            var before = RigidBodyState.Of(rb);
            rb.Shape = shapeIndex == 1 ? BodyShape.Sphere : BodyShape.Box;
            rb.MarkDirty();
            _entitySetLoader.SyncRigidBodyDefinition(e, rb);
            undo?.Push(new RigidBodyCommand(e, _entitySetLoader, before, RigidBodyState.Of(rb)));
        }

        if (rb.Kind != BodyKind.Dynamic) return;

        // Mass, unlike Kind/Shape, is a plain per-frame drag with no attach/detach side effect —
        // the closure below already performs the same MarkDirty()/Sync the RigidBodyCommand path
        // does, so it can go straight through the generic Widgets field-edit tracking (undo just
        // calls this same closure with the pre-drag value) rather than needing its own command.
        Widgets.DragRow("Mass", rb.Mass, v =>
        {
            rb.Mass = MathF.Max(0.001f, v);
            rb.MarkDirty();
            _entitySetLoader.SyncRigidBodyDefinition(e, rb);
        }, 0.05f, 0.001f, 10000f, "%.3f kg", 1f, undo);

        // Live physical state — read straight off RigidBody, refreshed every fixed step by
        // PhysicsSystem.StepFixed (Vector, then magnitude for a quick at-a-glance read). All zero
        // until physics.enabled and the first Sync() registers the body.
        ImGui.Spacing();
        ImGui.TextDisabled("Live");
        ReadOnlyRow("Velocity",     $"{Widgets.Vec3(rb.LinearVelocity)}  ({Widgets.Float(rb.LinearVelocity.Length())} m/s)");
        ReadOnlyRow("Angular Vel.", $"{Widgets.Vec3(rb.AngularVelocity)}  ({Widgets.Float(rb.AngularVelocity.Length())} rad/s)");
        ReadOnlyRow("Acceleration", $"{Widgets.Vec3(rb.LinearAcceleration)}  ({Widgets.Float(rb.LinearAcceleration.Length())} m/s²)");
    }

    private static void ReadOnlyRow(string label, string value)
    {
        ImGui.TextUnformatted(label);
        ImGui.SameLine();
        ImGui.TextDisabled(value);
    }
}
