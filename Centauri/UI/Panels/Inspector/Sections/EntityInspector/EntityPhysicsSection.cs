namespace Centauri.UI.Panels.Inspector.Sections;

using ImGuiNET;

using World;
using Common;
using Loading;
using Simulation.Physics;
using Editing.Undo;

// Attaches/edits/detaches a RigidBody on the selected entity. Shape is derived from the
// model's own bounds (see PhysicsSystem.Register) — there's nothing to author there beyond
// Box/Sphere/Capsule. Any edit after the initial attach calls RigidBody.MarkDirty() so
// PhysicsSystem tears down and recreates the BEPU body on its next Sync instead of silently
// keeping the old one; SyncRigidBodyDefinition mirrors the same edit into the entity's saved
// definition so it round-trips (see EntitySetLoader.Save — it just re-emits source.Components
// verbatim).
internal sealed class EntityPhysicsSection
{
    private static readonly string[] PhysicsKinds = ["None", "Dynamic", "Kinematic", "Static"];

    // Mesh is Static-only (see RigidBody.BodyShape.Mesh) — offering it for Dynamic/Kinematic would
    // let the dropdown show a shape the collider can never actually be built with. The Body combo
    // handler below resets Shape off Mesh the moment Kind leaves Static, so PhysicsShapesMovable
    // never needs a defensive clamp: whenever it's showing, rb.Shape is already guaranteed non-Mesh.
    private static readonly string[] PhysicsShapesStatic  = ["Box", "Sphere", "Capsule", "Mesh"];
    private static readonly string[] PhysicsShapesMovable = ["Box", "Sphere", "Capsule"];

    private readonly EntitySetLoader _entitySetLoader;

    public EntityPhysicsSection(EntitySetLoader entitySetLoader) => _entitySetLoader = entitySetLoader;

    public void Draw(Entity e, CommandHistory? undo)
    {
        using var s = Widgets.Section("Physics");
        if (!s.Open) return;

        var rb = e.GetComponent<RigidBody>();
        var kindIndex = rb switch
        {
            null                         => 0,
            { Kind: BodyKind.Kinematic } => 2,
            { Kind: BodyKind.Static }    => 3,
            _                            => 1
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

            var kind = kindIndex switch
            {
                2 => BodyKind.Kinematic,
                3 => BodyKind.Static,
                _ => BodyKind.Dynamic,
            };
            if (rb is null)
            {
                rb = e.AddComponent(new RigidBody { Kind = kind });
            }
            else
            {
                rb.Kind = kind;
                // Mesh only exists for Static (see RigidBody.BodyShape.Mesh) — leaving it set while
                // switching away would show "Mesh" in a dropdown that no longer offers it. Resetting
                // here, not just filtering the dropdown, keeps rb.Shape truthful to what's displayed.
                if (kind != BodyKind.Static && rb.Shape == BodyShape.Mesh)
                    rb.Shape = BodyShape.Box;
                rb.MarkDirty();
            }
            _entitySetLoader.SyncRigidBodyDefinition(e, rb);
            undo?.Push(new RigidBodyCommand(e, _entitySetLoader, before, RigidBodyState.Of(rb)));
        }

        if (rb is null) return;

        var shapeOptions = rb.Kind == BodyKind.Static ? PhysicsShapesStatic : PhysicsShapesMovable;
        var shapeIndex = rb.Shape switch
        {
            BodyShape.Sphere  => 1,
            BodyShape.Capsule => 2,
            BodyShape.Mesh    => 3,
            _                 => 0,
        };
        if (Widgets.ComboRow("Shape", ref shapeIndex, shapeOptions))
        {
            var before = RigidBodyState.Of(rb);
            rb.Shape = shapeIndex switch
            {
                1 => BodyShape.Sphere,
                2 => BodyShape.Capsule,
                3 => BodyShape.Mesh,
                _ => BodyShape.Box,
            };
            rb.MarkDirty();
            _entitySetLoader.SyncRigidBodyDefinition(e, rb);
            undo?.Push(new RigidBodyCommand(e, _entitySetLoader, before, RigidBodyState.Of(rb)));
        }

        // Friction applies to every Kind — a Static floor's surface matters as much as a Dynamic
        // crate's — unlike Mass and the live velocity readout below, which only mean anything for
        // a body the simulation actually moves under gravity/forces. No Bounciness row: see
        // RigidBody.Friction's own comment for why restitution isn't implemented this pass.
        Widgets.DragRow("Friction", rb.Friction, v =>
        {
            rb.Friction = MathF.Max(0f, v);
            rb.MarkDirty();
            _entitySetLoader.SyncRigidBodyDefinition(e, rb);
        }, 0.01f, 0f, 10f, "%.2f", 1f, undo);

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
