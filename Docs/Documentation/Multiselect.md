# Multi-select

The second Phase-1 editor-usability item after undo/redo — "Multi-select in the Outliner, at least
for bulk transform edits and delete" per `Docs/Roadmaps/ENGINE_ROADMAP.md`. Select more than one
entity at a time (Outliner Ctrl/Shift-click, or Ctrl+click in the viewport), then drag them all
together with the gizmo or delete them all at once — both as a single undo step.

## 1. `Scene`'s selection model

`Scene` used to hold a single nullable `Selected` entity. It now holds an ordered
`List<Entity> _selected` (insertion order, not scene order — see its own comments), exposed as:

- **`SelectedEntities`** — the whole set.
- **`Selected`** — the *primary* selection: the most recently added entity (`_selected[^1]`), or
  `null` if nothing's selected. Every call site that only cares about "the one entity" — the
  Properties panel's inspector, the gizmo's screen-anchor position — still reads this, unchanged.
- **`IsSelected(entity)`**, **`Select(entity)`** (replace the whole set with just this one, or
  clear it for `null`), **`ToggleSelect(entity)`** (Ctrl-click), **`AddToSelection(entity)`**
  (Shift-range-select, called in a loop — see §2), **`ClearSelection()`**.

`RemoveEntity` drops the removed entity from the selection (previously just null-checked the single
`Selected`), so deleting a selected entity — including via a bulk delete that's still iterating the
rest of the selection — never leaves a stale reference in the set.

## 2. Where selection changes: Outliner, viewport, and range-select

**Outliner** (`HierarchyPanel.DrawRow`) — plain click replaces the selection (unchanged); Ctrl-click
toggles the clicked row; Shift-click range-selects from a tracked `_selectionAnchor` (the row index
of the last plain- or Ctrl-click — Shift-clicks themselves don't move it, so repeated Shift-clicks
keep adjusting the same range rather than moving it each time, the Explorer/Blender convention) to
the clicked row, inclusive, **replacing** the selection with that range rather than adding to it.

**Viewport** (`InputSystem.PickAtCursor`) — plain click replaces the selection (unchanged);
Ctrl+click toggles whatever's under the cursor. No Shift-range-select here — there's no natural
"list order" to range over in a 3D view the way there is over the Outliner's rows. Ctrl+click on
empty space is a no-op (leaves the current selection alone) rather than clearing it, matching every
other app's "Ctrl+click missing everything" behavior.

## 3. Bulk gizmo transform (`TransformGizmo`)

The handles are still drawn anchored to the *primary* selection (`Scene.Selected`) — multi-select
doesn't change where the gizmo appears or how big it is. But a drag now moves/rotates/scales
**every** selected entity together: each by the *same* world-space delta / rotation angle / scale
factor, applied to its own frozen start state — not a true shared-pivot group transform, where
rotating or scaling a group would also revolve each entity's *position* around a shared centre.
That's a materially bigger feature (and wasn't what "bulk transform edits" in the roadmap's wording
was pointing at); this is the same "grab it, everyone moves/turns/grows the same amount, each in
place" a lot of editors offer as their default multi-select behavior.

`BeginLinearDrag`/`BeginRotateDrag` snapshot every selected entity's `TransformState` (+ world
position, needed for translate's math specifically — see `TransformState`) into `_dragGroup` the
moment a handle is grabbed. The shared reference geometry (drag axis, screen direction, world-per-
pixel scale, rotate radius/sign) is still derived from the primary entity only, same as before
multi-select existed — it's the *one* delta computation, just now applied to every entity in the
group instead of only the primary. `EndDrag` diffs each entity's before/after state and pushes one
`TransformCommand` per entity that actually changed, via `CommandHistory.PushRange` — wrapped in a
single `CompositeCommand` when more than one entity changed, so a multi-select drag is still one
Ctrl+Z, not one per entity.

## 4. Bulk delete (`InputSystem`)

The Delete-key handler now snapshots `Scene.SelectedEntities` (materialized to a list first —
`RemoveEntity` mutates the live selection as each entity is deleted, so iterating it directly would
skip entries), captures + deletes each one (`EntitySetLoader.Capture`/`DeleteEntity`, same as
before multi-select), and pushes the resulting `DeleteEntityCommand`s through `PushRange` — again
one Ctrl+Z undoes the whole batch.

## 5. `CompositeCommand` (`Editing/Undo/`)

The piece that makes both of the above "one gesture, one undo step": wraps a list of already-applied
`ICommand`s, running `Undo` in reverse order and `Redo` in original order. `CommandHistory.PushRange`
is the single entry point every bulk-edit call site uses — zero commands pushes nothing, exactly one
pushes it directly (so a single-entity edit isn't wrapped in a pointless `CompositeCommand` and still
undoes in one `Undo()` call, unchanged from before multi-select), more than one wraps them.

## 6. Visual feedback

`DebugRenderer.DrawSelection` now outlines every selected entity, not just the primary — otherwise a
multi-select would look and feel identical to a single-select with extra steps, since nothing on
screen would show what a Ctrl/Shift-click actually added. The Properties panel
(`EntityInspectorSection`) still only *edits* the primary entity — building real multi-entity
property editing (what a field shows/does when the same property differs across the selection) is
its own feature, out of scope here — but shows a `+N more selected` hint so it's visible there's
more selected than what's displayed, rather than silently looking like a single-select there too.

## 7. What's deliberately out of scope

- **Multi-entity property editing** in the Properties panel — see §6.
- **True shared-pivot group transforms** (rotate/scale revolving each entity's position around a
  common centre, not just each entity's own orientation/scale in place) — see §3.
- **Box-select** (drag a rectangle in the viewport to select everything inside it) — not asked for
  by the roadmap's wording, and viewport picking today is a single ray-cast per click
  (`Scene.Pick`), not built for a screen-space rectangle query.
- **Multi-select persistence** — which entities were selected isn't part of any `EntityDefinition`,
  so (like the pre-existing single selection) it doesn't survive a save/reload. Not a regression;
  selection was never part of the saved scene state.

## 8. Tests

Both new suites are pure C# (no ImGui/GL) — see `Docs/Documentation/Testing.md`'s table for the
one-line summary of each. `TransformGizmo`'s multi-select drag math and `InputSystem`'s bulk delete
aren't unit-tested directly — same constraint as the rest of the gizmo/InputSystem interaction path
(`Docs/Documentation/Gizmos.md` §3, `Docs/Documentation/Undo.md` §4): they need a live cursor or a
real `ResourceSystem`/GL context to build entities through. Verified instead by a headless boot
smoke test confirming the changed call sites (`Scene`, `TransformGizmo`, `HierarchyPanel`,
`InputSystem`, `DebugRenderer`) don't throw, plus code review — `Scene`'s selection-set semantics
themselves (the part every one of those call sites actually depends on) are what `SceneSelectionTests`
pins directly.
