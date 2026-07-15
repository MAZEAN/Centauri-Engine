# Transform Hierarchy

`Transform` (`World/Transform.cs`) has always supported parent/child linking and world-matrix
composition through it — `Parent`, `Children`, `WorldMatrix = LocalMatrix * Parent.WorldMatrix`,
with a cycle guard on the `Parent` setter. What was missing, closed by this change, was any way to
*author* it: the entity-set JSON schema had no parent field, and the inspector had no UI for it.

## 1. Author a hierarchy

### From the editor

Select a child entity → Inspector → **Hierarchy** → **Parent** combo. Lists every other entity in
the scene except the selected one and anything already a descendant of it (picking one of those
would create a cycle — excluded from the list up front rather than offered and then silently
refused). Selecting `(None)` moves the entity to the scene root.

**No world-position-preserving compensation** — reparenting doesn't adjust Position/Rotation/Scale
to compensate for the new parent's transform, so the entity visibly jumps unless its local
transform already made sense relative to the new parent. This is a deliberate scope cut for this
first pass (see §4); author child positions with the intended parent already in mind.

### From entity-set JSON

```jsonc
{
  "entities": [
    { "name": "Turret",   "position": [0, 1, 0] },
    { "name": "Barrel",   "position": [0, 0.5, 0], "parent": "Turret" }
  ]
}
```

`parent` is resolved by `name` against every other entity **in the same file only** — see §3 for
why. Order within the file doesn't matter: a child may appear before or after its parent in the
array (`EntitySetLoader.LoadAll` builds every entity in a file first, then wires parents in a
second pass — `WireHierarchy`). A `parent` naming a nonexistent entity is silently ignored (the
entity loads at the scene root, not an error) — useful during authoring/refactoring, since a typo
or a since-deleted parent doesn't break loading the rest of the file.

## 2. How it round-trips

`EntitySetLoader.ToDefinition` (called by `Save()`) re-derives each entity's `parent` **live** from
`entity.Transform.Parent` at save time — via `FindTrackedOwner`, an O(n) scan over this loader's
tracked entities — rather than trusting a cached name. `EntitySetLoader.SetParent` (the inspector's
write path) deliberately doesn't write anything back into the tracked `EntityDefinition` itself for
the same reason: the live `Transform` graph is the actual source of truth, and a cached copy is
just one more thing that could drift out of sync with it.

`DeleteEntity` promotes any children to the scene root (`Transform.Parent = null`) before disposing
the deleted entity — `Entity.Dispose()` doesn't clear `Transform`, so a still-linked child would
otherwise keep computing a valid `WorldMatrix` through a Transform whose owning entity no longer
exists in the scene. Explicit unparenting avoids that dangling-but-functional state entirely,
rather than either cascade-deleting the whole subtree or leaving it silently orphaned.

## 3. Why same-file-only parenting

`EntitySetPaths` load in config order, but `Render.DefaultEntitySetPath` (where live-created
entities land) can load before or after other configured files depending on whether it's already
listed — there's no guaranteed topological order between files the way there now is *within* one
file (see §1's two-pass note). Scoping parent resolution to one file avoids depending on load order
between files ever being consistent, at the cost of not being able to parent an entity in one file
to an entity defined in another. Revisit if that limitation actually bites — it's not a fundamental
one, just narrower than a full engine-wide reference scheme would need to be.

## 4. Known limitations / next steps

Deliberately scoped as a foundation, same discipline as `PhysicsEngine.md`/`LocalShadows.md`:

- **No world-position-preserving reparent** — see §1. Would need computing a new local
  Position/Rotation/Scale from `inverse(newParentWorldMatrix) * oldWorldMatrix` at the moment of
  reparenting; skipped this pass to keep `EntitySetLoader.SetParent` simple. Worth adding once
  reparenting existing (not just newly-authored) content becomes a common workflow.
- **No Outliner tree view** — the Hierarchy panel (`HierarchyPanel.cs`) still lists every entity
  flat; children aren't indented under their parent, and there's no drag-and-drop reparenting.
  The Inspector's Parent combo is the only authoring UI this pass. Worth adding once flat lists
  get unwieldy for hierarchies deep/wide enough to need visual structure.
- **No cross-file parenting** — see §3.
- **Doesn't yet unblock the TODO items that motivated it** — "wind (hierarchy) & leaves
  (orientation)" and "link leaves to branches in the wind" still need something to actually
  *consume* the hierarchy (e.g. a wind-sway component reading a leaf's parent branch's sway state)
  — this change is the prerequisite (a leaf can now be a child of its branch), not that consumer
  itself.
