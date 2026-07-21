# Viewport Gizmos (`UI/Gizmos/`)

Interactive transform handles drawn over the selected entity in the viewport — the first
Phase-1 editor-usability item from `Docs/Roadmaps/ENGINE_ROADMAP.md`. Currently: a **translate**
gizmo (`UI/Gizmos/TransformGizmo.cs`). Rotate/scale are designed-for but not yet built (see
§4).

## 1. Why not ImGuizmo

The roadmap named ImGuizmo (the usual Dear ImGui pairing). We deliberately didn't use it:

- **Native dependency.** `ImGuizmoNET` needs a per-RID native `cimguizmo` binary that isn't
  bundled. This repo's CI now builds on a **four-way OS/arch matrix** (Linux/Windows × x64/arm64)
  plus a headless Xvfb job — every one of those would need that native asset resolved correctly,
  which is exactly the class of failure the GLFW native-load saga (see git log around the CI work)
  just cost several rounds to sort out.
- **ABI risk.** `ImGuizmoNET` ships its own `ImGuiNET`, which can clash with the copy
  `Silk.NET.OpenGL.Extensions.ImGui` already bundles.
- **Ethos.** The engine is "built directly on OpenGL, no external game-engine framework" — and we
  already have every piece needed (full camera matrices, `Camera.ScreenPointToRay`, `Transform`,
  and ImGui's foreground draw list for a 2D overlay).

So the gizmo is ~200 lines of our own projection + hit-test + drag math instead.

## 2. How it works

`TransformGizmo` is owned by `UISystem` and its `Draw(scene, camera)` is called once per frame,
**only in Edit mode with a selection** (the same block that renders the Outliner/Properties). It:

1. Takes the selected entity's world position (`Transform.WorldPosition`) as the gizmo origin.
2. Projects the origin and three axis endpoints (world ±X/Y/Z, a distance-scaled length so the
   gizmo stays a roughly constant on-screen size) to screen space with the **raw** projection
   (`GetViewMatrix() * GetProjectionMatrixRaw()` — raw so the handles don't inherit the scene's
   TAA jitter).
3. Draws the arrows + a centre dot into `ImGui.GetForegroundDrawList()` — a pure 2D overlay, **no
   GL render-graph involvement, no new pass**.
4. Runs all interaction off ImGui's own IO mouse state during that same frame (hover-test →
   click-to-grab → drag → release), so `InputSystem` needs to know nothing about it.

### Coordinate conventions

Projection mirrors `Camera.ScreenPointToRay` exactly — row-vector `point * (view*proj)`,
perspective divide, and the same NDC→screen Y-flip. This agreement is **load-bearing**: the gizmo
*draws* handles with `Project` and the viewport *picks* entities with `ScreenPointToRay`; if the
two ever disagreed, handles would render in one place and respond to the cursor in another. The
test suite pins them together (§3).

### Dragging, parent-aware

On grab, the reference geometry (start mouse pos, the origin's start world position, the axis's
screen direction, and a world-units-per-pixel scale) is **frozen** so the mapping doesn't drift as
the object moves mid-drag. Each frame the along-axis mouse delta becomes a world-space translation,
which is written back through the parent to local space:

```
WorldPosition = Transform(localPosition, parentWorldMatrix)
  ⇒ localPosition = Transform(desiredWorld, inverse(parentWorldMatrix))
```

No parent (or a non-invertible one) collapses to setting the world position directly. Because the
drag writes `Transform.Position`, everything downstream that already reacts to a transform change
comes along for free — the inspector's Location rows update live, and `Transform.OnChanged` still
fires (physics-body rebuild, world-matrix dirtying, etc.).

### The InputSystem handshake

The gizmo isn't an ImGui *window*, so it doesn't raise `WantCaptureMouse`. Instead
`TransformGizmo.IsInteracting` (hovering a handle **or** mid-drag) is folded into
`UISystem.WantsMouse`, which `InputSystem` already consults before ray-picking — so clicking a
handle grabs it instead of re-picking whatever entity is behind it. This is the same
one-frame-latency pattern the existing `WantCaptureMouse` gating already relies on. Conversely,
while an ImGui panel wants the mouse, the gizmo suppresses its own hover so a handle behind a panel
can't steal that panel's clicks.

## 3. Tests

`Centauri.Tests/UI/TransformGizmoTests.cs` — the projection and hit-test are pure math (no
ImGui/GL), reachable via `internal` + `InternalsVisibleTo`. Covers:

- A front-of-camera point landing inside the viewport (and dead-centre framing hitting the middle).
- A behind-camera point returning `false` (w ≤ 0).
- World axes mapping to the expected screen directions (+X right, +Y *up* = screen-Y down).
- **The round-trip invariant**: a world point → `Project` → screen pixel → `ScreenPointToRay` →
  a ray that passes back through the original point. This is the one that catches a silent
  sign/axis flip between drawing and picking.
- `DistanceToSegment` against hand-computed geometry (on-segment, perpendicular, past either end).

Interaction (the actual mouse drag) isn't unit-tested — it needs a live ImGui IO frame — but was
verified visually headless (force Edit mode + auto-select, render, confirm handles land on the
selection; that scaffolding is env-var-gated and not committed).

## 4. Extending to rotate / scale

The `Draw` → project-origin → per-axis project/hit-test/drag scaffold is mode-agnostic. Rotate
wants screen-space arcs and an angular drag (cross-product sign off the origin) instead of arrows
and a linear delta; scale reuses the translate arrows almost verbatim but multiplies
`Transform.Scale` along the axis instead of adding to `Position`. A mode enum + a hotkey to switch
(W/E/R is the Blender-ish convention) is the natural next step, plus a local-vs-world toggle (the
current gizmo is world-axis only).
