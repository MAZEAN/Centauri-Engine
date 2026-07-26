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
  drag.
- **`CreateEntityCommand`** / **`DeleteEntityCommand`** — one Outliner "+ Add" / one Delete-key
  press.

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

## 3. What's deliberately out of scope (this pass)

- **Inspector field edits** — the Location/Rotation/Scale/Uniform-Scale drag rows in
  `EntityTransformSection`, material property edits (Color/Roughness/Metallic/Translucency),
  rigidbody edits, and re-parenting via the Hierarchy section's parent picker aren't undoable yet.
  Doing this properly needs `Widgets`' `DragRow`/`Vec3Rows` helpers to expose ImGui's own
  activation/deactivation state (`IsItemActivated`/`IsItemDeactivatedAfterEdit`) to the caller,
  which none of them do today — a real follow-up, not a quick add.
- **A deleted entity's *former children*** — `DeleteEntity` already promotes a deleted entity's
  children to the scene root as a permanent side effect *before* the delete command even captures
  anything; undoing the delete brings the entity itself back but doesn't re-link those children to
  it. Re-establishing that would mean capturing the pre-delete child list too and re-parenting them
  back on undo — deferred as out of scope for a first, coarse pass.
- **No undo/redo buttons** — keyboard-only (Ctrl+Z/Ctrl+Y), same as Ctrl+S/Ctrl+Shift+R having no
  UI button either. `CommandHistory.CanUndo`/`CanRedo` exist and are ready for a future toolbar
  affordance if one's wanted.

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
