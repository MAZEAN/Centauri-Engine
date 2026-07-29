# Undo/Redo (`Editing/Undo/`)

Ctrl+Z / Ctrl+Y — the first Phase-1 editor-usability item from `Docs/Roadmaps/ENGINE_ROADMAP.md`
after viewport gizmos and the docked editor layout. Before this, the only "undo" was
`EntitySetLoader.Reset()` (Ctrl+Shift+R) — discard every live edit and reload from disk. This is a
real, if deliberately **coarse**, undo stack: one step per *completed* edit gesture (a finished
gizmo drag, an entity create/delete), not a per-frame or per-keystroke diff — exactly the scope the
roadmap itself invited ("even a coarse one — snapshot/diff per edit gesture").

## 1. The Command pattern

`Editing/Undo/` is a small, self-contained namespace:

- **`ICommand`** — `Undo()` / `Redo()`. By the time a command is constructed the edit has already
  happened (a drag already moved the entity live, frame by frame, while the mouse was down; a
  click already created the entity) — a command just captures enough to reverse or replay it.
- **`CommandHistory`** — the bounded (200-entry) undo/redo stack. `Push` records an
  already-applied command (never calls `Undo`/`Redo` itself); `Undo`/`Redo` pop one stack, run the
  command, and push it onto the other. A fresh `Push` after an `Undo` clears the redo stack, same
  as every other undo system. Pure C#, no ImGui/GL — unit-tested directly against a spy `ICommand`
  (`Centauri.Tests/Editing/CommandHistoryTests.cs`).
- **`TransformCommand`** (+ `TransformState`, its before/after snapshot) — one completed gizmo
  drag, or one completed inspector Transform-section gesture (§2).
- **`CreateEntityCommand`** / **`DeleteEntityCommand`** — one Outliner "+ Add" / one Delete-key
  press.
- **`FieldEditCommand<T>`** — one completed inspector field edit outside the Transform section
  (Material properties and RigidBody Mass, §2) — a drag-to-release, a slider hop, a checkbox toggle,
  or a right-click "Reset," generic over the field's own type rather than one command class per
  field.
- **`RigidBodyCommand`** (+ `RigidBodyState`, its Kind/Shape before/after snapshot) — one completed
  Body-kind or Shape combo change in `EntityPhysicsSection` (attach, detach, or switch
  Dynamic/Static/Box/Sphere).
- **`ReparentCommand`** — one completed Parent-picker selection in `EntityHierarchySection`.

One `CommandHistory` instance lives on `Engine` (constructed in `InitializeSystems`, alongside the
other top-level systems) and is threaded into both `InputSystem` (Ctrl+Z/Ctrl+Y, and the Delete-key
handler that now also captures a `DeleteEntityCommand`) and `RenderingSystem.InitializeComponents` →
`UISystem` → `TransformGizmo` / `HierarchyPanel` — the two places edits actually originate. Nothing
else reaches into it directly.

## 2. What's covered

**Gizmo drags** (`TransformGizmo`) — `BeginLinearDrag`/`BeginRotateDrag` snapshot the Transform's
full Position/Rotation/Scale (`TransformState.Of`) the moment a handle is grabbed; the shared
`EndDrag`, called from both `LinearMode` and `RotateMode` on mouse-release, compares against the
live state and pushes one `TransformCommand` for the whole gesture *if anything actually changed*
(a click-release with no movement in between isn't worth an undo step). `Undo`/`Redo` restore all
three of Position/Rotation/Scale, not just whichever one the active mode changed — simpler than
threading "which field changed" through the command, and harmless, since the other two just get set
back to the value they already had. Restoring rotation goes through `Transform.SetRotation` rather
than the raw `Rotation` setter specifically to keep the `EulerAngles` cache the inspector reads from
coherent — see `Docs/Documentation/Gizmos.md` §"Rotate: keeping the inspector coherent" for why that
matters, and `TransformCommandTests` for the regression test pinning it.

**Entity create/delete** — `HierarchyPanel`'s "+ Add" pushes a `CreateEntityCommand` right after
calling `EntitySetLoader.CreateEntity`; `InputSystem`'s Delete-key handler calls the new
`EntitySetLoader.Capture(entity)` (the same `EntityDefinition` snapshot `Save()` would write, plus
the entity's tracked source file) *before* deleting, and pushes a `DeleteEntityCommand` wrapping it.
`DeleteEntityCommand.Undo` calls the new `EntitySetLoader.Restore(definition, sourcePath)`, which
rebuilds the entity from that definition and re-parents it if the definition names a parent still
present in the scene (first name match wins, same tiebreak `EntityHierarchyWiring` uses at load
time).

**Inspector field edits — Transform and Material sections.** Previously the biggest practical gap
(§3 below used to list this as deferred): dragging a Location/Rotation/Scale row, a material
Color/Roughness/Metallic/Translucency/UV/Two-Sided/Wind/Triplanar/Parallax field, or typing a value
into one, now pushes an undo step exactly like a gizmo drag or a Delete does.

*Widgets* (`UI/Common/Widgets.cs`) is where this actually happens — every row helper
(`DragRow`/`SliderRow`/`ColorRow3`/`ColorRow4`/`CheckRow`/`Vec2Row`) now takes an optional trailing
`CommandHistory? undo = null`. The ~80 other call sites (PostFX/Reflections/Environment/Scene
config panels — global render settings, not entity state, and out of scope for this feature) all
leave it `null` and behave exactly as before. Only `EntityMaterialSection` passes a real
`CommandHistory` (threaded in from `UISystem` → `PropertiesPanel` → `EntityInspectorSection`,
alongside the existing `EntitySetLoader`/`ResourceSystem` wiring), and pushes one `FieldEditCommand<T>`
per completed field gesture via two static helpers, `TrackEditBegin`/`TrackEditEnd`, keyed by the
row's ImGui id string:

- **`TrackEditBegin`** captures the field's value the first frame it *isn't* already pending an
  edit — not on `ImGui.IsItemActivated()` directly, because a click-type widget (`Checkbox`, a
  slider hop) can mutate its value the very frame it activates, which would already be one frame
  too late to read the pre-click state. Gating on "no pending entry yet" means a multi-frame drag's
  captured "before" is the value from drag-*start*, not merely a frame ago.
- **`TrackEditEnd`** commits — pushes a `FieldEditCommand<T>` comparing that captured value against
  the current one — the first frame the widget is no longer `ImGui.IsItemActive()`, i.e. release.
  A right-click "Reset" doesn't go through this pair at all (it's a separate popup-menu widget, not
  a drag/click on the primary one) — handled directly by a small `ResetRow` wrapper that pushes its
  own `FieldEditCommand` comparing pre-reset to post-reset value.

`EntityTransformSection` (Location/Rotation/Scale/Uniform Scale) takes a different path: rather than
one `FieldEditCommand<float>` per axis, it reuses `TransformCommand`/`TransformState` — the exact
same command `TransformGizmo` pushes for a viewport drag. It snapshots the whole `Transform`
speculatively at the start of every frame the section was idle last frame (cheap — a value-type
struct copy, discarded if no drag actually starts), tracks "is any of the four rows currently
active" across the frame, and pushes one `TransformCommand` for the whole gesture once every row
goes back to idle — so dragging Position then immediately Scale, say, without the mouse ever fully
leaving the section, is still one Ctrl+Z, matching how the gizmo already treats a multi-axis drag as
one step. This also means Transform-row edits share every property `TransformCommand` already has —
including restoring rotation through `Transform.SetRotation` so the inspector's own `EulerAngles`
cache stays coherent (§2's opening paragraph above).

**RigidBody edits and re-parenting.** The two pieces named as still-deferred by the previous round
of this document — closed out this pass.

`EntityPhysicsSection`'s Body-kind combo (None/Dynamic/Static) and Shape combo (Box/Sphere) don't
fit `FieldEditCommand<T>`: attaching or detaching the `RigidBody` component is a structural change,
not a value swap, and every edit — attach, detach, or a Kind/Shape change on an already-attached
body — has to replay two side effects the live edit path itself performs: `RigidBody.MarkDirty()`
(so `PhysicsSystem` tears down and rebuilds the BEPU body on its next `Sync` instead of silently
keeping the stale one) and `EntitySetLoader.SyncRigidBodyDefinition` (so the saved definition stays
in step with the live component). `RigidBodyCommand` captures the whole before/after `RigidBodyState`
(Kind + Shape; `null` for "no `RigidBody` attached") and replays both side effects on *either*
direction — `Undo` isn't just "put the old value back," it's "reapply the same attach/detach/rebuild
path the original edit used, aimed at the old state." Mass, by contrast, doesn't attach or detach
anything — it's a plain per-frame drag whose `set` closure already calls `MarkDirty()`/`Sync` on
every value, old or new — so it goes straight through the generic `Widgets`/`FieldEditCommand<float>`
mechanism above rather than needing its own command.

`EntityHierarchySection`'s Parent combo is simpler: one `EntitySetLoader.SetParent` call per
selection, so `ReparentCommand` just replays that same call with the old or new parent — no separate
before/after state type needed, and (since `SetParent` itself refuses a cycle rather than assuming
its caller already filtered one out) a redo that would recreate a cycle in some scene that changed
shape since the original edit is refused the same way the live edit was, rather than corrupting the
hierarchy.

## 3. What's deliberately out of scope (this pass)

- **A deleted entity's *former children*** — `DeleteEntity` already promotes a deleted entity's
  children to the scene root as a permanent side effect *before* the delete command even captures
  anything; undoing the delete brings the entity itself back but doesn't re-link those children to
  it. Re-establishing that would mean capturing the pre-delete child list too and re-parenting them
  back on undo — deferred as out of scope for a first, coarse pass.
- **No undo/redo buttons** — keyboard-only (Ctrl+Z/Ctrl+Y), same as Ctrl+S/Ctrl+Shift+R having no
  UI button either. `CommandHistory.CanUndo`/`CanRedo` exist and are ready for a future toolbar
  affordance if one's wanted.

### A known gap in `FieldEditCommand`'s tracking key

`Widgets`' `TrackEditBegin`/`TrackEditEnd` (§2) key their pending-edit dictionary by the row's plain
label string (e.g. `"##Roughness"`) — `Widgets` is a stateless static class with no per-entity or
per-slot context to fold into the key. Two different entities' (or two different mesh slots')
same-named field safely reuse that key *sequentially*, since only one ImGui widget can be active at
a time — but a drag abandoned mid-gesture by something other than releasing the mouse over it (e.g.
pressing Ctrl+Shift+R while a slider is still held down) can leave a stale pending entry that a
later, unrelated field with the same label picks up, producing one mismatched undo/redo pair on
that field. Rare, and the failure mode is a wrong-ish value recoverable by editing again or hitting
redo — not data corruption — so this was accepted rather than threading extra per-field identity
through every `Widgets` call site for a first pass.

### A note on object identity across delete/recreate

Every command holds a **direct reference** to the `Entity`/`Transform` it operates on, not a stable
id — `Entity` has no such id today (only `Name`, which isn't guaranteed unique). `CreateEntityCommand`
and `DeleteEntityCommand` both replace their `Entity` reference with a **new** instance on `Redo`
(create) or `Undo` (restore), since `EntitySetLoader.DeleteEntity` disposes the old one outright
(`Entity.Dispose` unsubscribes its own `Transform.OnChanged` handler — it isn't designed to come
back from that). This is fine as long as later commands don't hold a *separate* stale reference to
an entity that got deleted-then-recreated in between: e.g. drag an entity (`TransformCommand` on
Transform A) → delete it (`DeleteEntityCommand`, capturing the post-drag position) → undo the delete
(recreates as Transform B, at the captured position) → undo the drag (still references stale
Transform A — a no-op on the object nothing renders anymore, not Transform B). Real, but a known
limitation of this first, coarse pass rather than something silently wrong — a more robust version
would key commands by a stable entity id instead of an object reference, which doesn't exist yet.
`CommandHistory.Clear()` (called on `EntitySetLoader.Reset()`, Ctrl+Shift+R) sidesteps the same
class of problem for a full scene reload by invalidating the whole stack at once rather than trying
to remap every reference.

## 4. Tests

Both new suites are pure C# (no ImGui/GL) — see `Docs/Documentation/Testing.md`'s table for the
one-line summary of each. `CreateEntityCommand`/`DeleteEntityCommand` aren't unit-tested directly
(they need a real `ResourceSystem`/GL context to build entities through `EntitySetLoader`, the same
constraint that keeps `EntitySetLoader` itself out of the pure-CPU suite) — verified instead by a
headless boot smoke test confirming the full `Engine` → `InputSystem`/`RenderingSystem` → `UISystem`
→ `TransformGizmo`/`HierarchyPanel` `CommandHistory` wiring doesn't throw, plus code review. The
gizmo-drag and Outliner-create/Delete-key interaction itself needs a live cursor, so — same as the
gizmo's own drag feel (`Docs/Documentation/Gizmos.md` §3) — it rests on the math/logic tests above
rather than being exercised interactively here.

`FieldEditCommand<T>` and `Widgets`' `TrackEditBegin`/`TrackEditEnd`/`ResetRow` aren't unit-tested
either, for the same reason plus one more: they need a live ImGui frame (`ImGui.IsItemActive()` etc.
only mean anything mid-frame, right after the widget they refer to was drawn), which the pure-C#
suite has no way to fake short of reimplementing ImGui's item-state machine. Verified by a headless
boot smoke test (full `Engine` → `UISystem` → `PropertiesPanel` → `EntityInspectorSection` →
`EntityTransformSection`/`EntityMaterialSection` → `Widgets` wiring boots without throwing) plus code
review; the actual drag-then-release undo behavior — same as the gizmo's and the Outliner's own
interaction paths above — needs a live cursor to exercise and wasn't interactively verified this
pass.

`RigidBodyCommand` and `ReparentCommand` are the same story again: both need a real `EntitySetLoader`
(GL-backed `ResourceSystem`) and a live combo interaction to exercise, so — like
`CreateEntityCommand`/`DeleteEntityCommand` before them — they're verified by a headless boot smoke
test of the full `Engine` → `UISystem` → `PropertiesPanel` → `EntityInspectorSection` →
`EntityPhysicsSection`/`EntityHierarchySection` wiring plus code review, not a live click-drag-undo
interaction.
