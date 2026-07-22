# Viewport Gizmos (`UI/Gizmos/`)

Interactive transform handles drawn over the selected entity in the viewport — the first
Phase-1 editor-usability item from `Docs/Roadmaps/ENGINE_ROADMAP.md`. All three modes —
**translate / rotate / scale**, switched with **W / E / R** or the on-screen mode bar — on one
shared project → hit-test → drag scaffold.

`UI/Gizmos/` is split by responsibility:

- **`TransformGizmo`** — the interaction coordinator: mode, hover/drag state, turning mouse motion
  into `Transform` edits.
- **`GizmoMath`** — pure geometry (projection, segment/ring hit-tests, plane basis, world-rotation
  compose, the rotate-drag angle map). No ImGui/GL/state; this is what the unit tests target.
- **`GizmoDraw`** — everything touching the ImGui foreground draw list (arrows, scale boxes, rings,
  centre dot, axis colours), handed already-computed screen geometry.
- **`GizmoModeBar`** — the bottom-centre icon strip (see §2).

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

So the gizmo is our own projection + hit-test + drag math instead.

## 2. How it works

`TransformGizmo` is owned by `UISystem` and its `Draw(scene, camera)` is called once per frame,
**only in Edit mode with a selection** (the same block that renders the Outliner/Properties). It:

1. Takes the selected entity's world position (`Transform.WorldPosition`) as the gizmo origin.
2. Projects the origin and each handle (three axis endpoints for translate/scale, three rings for
   rotate — sized by a distance-scaled world length so the gizmo stays a roughly constant on-screen
   size) to screen space with the **raw** projection (`GetViewMatrix() * GetProjectionMatrixRaw()` —
   raw so the handles don't inherit the scene's TAA jitter).
3. Draws into `ImGui.GetForegroundDrawList()` — a pure 2D overlay, **no GL render-graph
   involvement, no new pass**.
4. Runs all interaction off ImGui's own IO mouse **and keyboard** state during that same frame
   (mode switch → hover-test → click-to-grab → drag → release), so `InputSystem` needs to know
   nothing about it.

### Modes and axis frames

- **Translate** (`W`) — three arrows along the **world** X/Y/Z; drag slides `Transform.Position`
  along the grabbed axis.
- **Rotate** (`E`) — three rings, one per **world** axis, each sampled to a projected polyline so
  its perspective ellipse is hit-tested and drawn correctly (not faked as a flat circle); drag
  spins the orientation about that world axis.
- **Scale** (`R`) — three box-tipped handles along the object's **local** basis (its world-rotated
  X/Y/Z), because `Transform.Scale` is local — a world-axis scale of a rotated object isn't
  representable by it. Drag multiplies that local axis's scale. (For an unrotated object the local
  basis equals the world axes, so it looks like translate with box tips.)

Mode switching reads `W`/`E`/`R` off ImGui IO, gated on no text field wanting the keyboard and no
modifier held (so `Ctrl+Shift+R`'s scene-reset doesn't also trip scale mode), and is ignored
mid-drag. `DebugHotkeys` (M/C/B/N/G) and camera fly (WASD, Fly-mode only) don't overlap. A
**`GizmoModeBar`** (bottom-centre of the viewport) mirrors this: three vector-drawn icon buttons
(Move / Rotate / Scale) that highlight the active mode and set it on click — the same
`TransformGizmo.ActiveMode` the keys drive. Bottom-centre because the left column is the
StatsOverlay and the right is the Outliner/Properties; icons are drawn with draw-list primitives
since no icon font is loaded.

### Rotate: keeping the inspector coherent

Rotate composes an arbitrary world-axis delta onto the grabbed orientation
(`ComposeWorldRotation` — a world rotation *pre*-multiplies in System.Numerics' convention, pinned
by a test) and writes it via **`Transform.SetRotation`**, which also refreshes the `EulerAngles`
cache the inspector's Rotation rows display and edit from. Without that refresh the inspector would
show a stale angle and its next drag would snap the object back to it. The quaternion→euler
extraction matches `CreateFromYawPitchRoll`'s Y·X·Z convention and pins roll to 0 at the ±90° pitch
gimbal; it's round-trip tested (rebuild a quaternion from the cached euler, assert same orientation).

### Rotate: the drag→angle mapping (why not exact atan2)

The obvious map — the angle the cursor has swept *around* the gizmo centre,
`atan2(cur−centre) − atan2(grab−centre)` — tracks a cursor circling the pivot perfectly but
decelerates hard on the straight drags people actually do: as the cursor pulls away from the centre
the effective radius grows, so each pixel turns the object less and less. `RotationAngleDelta`
instead uses the **linear (first-order) approximation** of that map, frozen at the grab: since the
derivative of `atan2` along the tangent is `cross(radialHat, ·)/radius`, this keeps the *initial*
rate and sign identical while staying constant-rate as the drag grows. Radial motion contributes
nothing and a radius floor stops a grab near the centre from becoming hypersensitive; `RotateGain`
is a one-number feel knob. Trade-off: a cursor circling the pivot no longer tracks 1:1 past large
angles (re-grab to continue), which is the far less common gesture.

### Coordinate conventions

Projection mirrors `Camera.ScreenPointToRay` exactly — row-vector `point * (view*proj)`,
perspective divide, and the same NDC→screen Y-flip. This agreement is **load-bearing**: the gizmo
*draws* handles with `Project` and the viewport *picks* entities with `ScreenPointToRay`; if the
two ever disagreed, handles would render in one place and respond to the cursor in another. The
test suite pins them together (§3).

### Dragging, parent-aware (translate)

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

The pure math now lives in `GizmoMath` (no ImGui/GL), reachable via `internal` + `InternalsVisibleTo`:

`Centauri.Tests/UI/TransformGizmoTests.cs` (targets `GizmoMath`):
- A front-of-camera point landing inside the viewport (and dead-centre framing hitting the middle).
- A behind-camera point returning `false` (w ≤ 0).
- World axes mapping to the expected screen directions (+X right, +Y *up* = screen-Y down).
- **The projection round-trip**: a world point → `Project` → screen pixel → `ScreenPointToRay` →
  a ray that passes back through the original point. Catches a silent sign/axis flip between
  drawing and picking.
- `DistanceToSegment` against hand-computed geometry (on-segment, perpendicular, past either end).
- **`ComposeWorldRotation`** — that a world-axis delta acts in the *world* frame regardless of the
  object's current orientation. This one *caught the bug* it guards: the initial `start * delta`
  multiply order was backwards for System.Numerics and the test failed until it was flipped to
  `delta * start`.
- **`RotationAngleDelta`** — that the rotate-drag mapping is *linear* in the tangential distance
  (2×/3× the travel → 2×/3× the angle, none of the atan2 taper), ignores radial motion, agrees with
  the exact map for small drags, and honours sign/gain.

`Centauri.Tests/World/TransformTests.cs`:
- **`SetRotation` euler coherence** — across a range of angles (and at the pitch gimbal), rebuild a
  quaternion from the euler the setter cached and assert it's the same orientation (|dot| ≈ 1),
  which sidesteps the many-valid-triples ambiguity of comparing angles directly.

Interaction (the actual mouse drag) isn't unit-tested — it needs a live ImGui IO frame — but the
**drawing** of all three modes was verified visually headless (force Edit mode + auto-select + a
non-trivial rotation, render each mode, confirm handles/rings land on the selection and scale's
handles follow the tilted local basis; that scaffolding is env-var-gated and not committed). The
drag **feel/sign** for rotate and scale — which genuinely can't be exercised without a cursor —
rests on the math + tests above and is worth an interactive sanity-check.

## 4. Possible extensions

- A **local-vs-world toggle** for translate/rotate (currently world-only; scale is always local).
- **Snapping** (hold a modifier to quantize to fixed translate/rotate/scale increments).
- A **plane handle** (the little quad between two axes) to translate/scale in a plane at once.
- **Uniform scale** via the centre handle (drag scales all three axes together).
