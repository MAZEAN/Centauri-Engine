# Editor Layout (`UI/Layout/`, `UI/Panels/Toolbar/TopBar.cs`)

The editor's panel arrangement — fully docked, resolution-independent, and split into three
Blender-style **workspaces** (`Edit` / `Performance` / `Viewing`) picked from the top bar. This
replaced the earlier floating-card layout: no panel positions itself anymore, and no two panels
overlap or leave a gap between them.

## 1. Workspaces

**Edit** — the working layout: left tool column (gizmo mode), 3D viewport, right sidebar
(Outliner above Properties). Only shown while the camera is actually in Edit mode (not Fly) — while
flying, the cursor is captured for camera look, so there'd be nothing to click these panels with.

![Edit workspace, 1920x1080](images/editor-layout-edit.png)

**Performance** — Statistics (full detail, not the trimmed table it used to share with the graphs)
on the left, the frame-time/GPU-timing graphs filling the rest. This is the payoff of splitting
`PerformancePanel` out of `StatsOverlay`: the graphs finally get real width instead of being
squeezed into a 350px card.

![Performance workspace, 1920x1080](images/editor-layout-performance.png)

**Viewing** — nothing but the top bar. For presenting, recording, or just looking at the scene.

![Viewing workspace, 1920x1080](images/editor-layout-viewing.png)

The same Edit workspace at 1024×768, to show the tiling holds at a smaller resolution too (see §3):

![Edit workspace, 1024x768](images/editor-layout-edit-1024x768.png)

Workspace tabs live in `TopBar` (renamed from the old `ViewportToolbar` — its role grew from "the
shading-mode strip" to "the whole top bar") and are independent of `Config.ViewMode` (Fly vs. Edit
camera behavior) — you can be in the Edit *workspace* while the camera is in Fly *mode*; `UISystem`
hides the interactive panels in that combination rather than `EditorLayout` knowing about camera
state at all.

## 2. `EditorLayout` — pure, tested geometry

`EditorLayout.Compute(workspace, workPos, workSize, uiScale)` turns a workspace + the current
viewport rect into exact docked rects — no ImGui calls, no mutable state, just arithmetic on its
arguments. Every panel is handed the `LayoutRect` it should occupy for the frame (`PanelHost.Place`
sets `SetNextWindowPos`/`SetNextWindowSize` from it) instead of positioning itself, which is what
makes "no gaps, no overlaps" a property of one function instead of something to keep in sync across
five panels' independent `SetupWindow` methods.

The Edit workspace's five regions (`TopBar`, `LeftTools`, `Viewport`, `Outliner`, `Properties`) are
built from one shared chain of intermediate values (`bodyY`/`bodyH`, `toolColW`, `sidebarW`,
`viewportW`) rather than each computed independently — a neighbour's shared edge is therefore the
literal same floating-point value on both sides, not two independently-rounded numbers that happen
to be close. `Outliner`/`Properties` split the sidebar height 35/65 (`OutlinerFrac`), touching with
no gap.

Small resolutions are clamped, not allowed to go negative: the tool column (small, effectively
never clamped) is reserved first, then the sidebar takes whatever's left after reserving a
`MinViewportW` floor for the render, so a docked sidebar can shrink but can never fully swallow the
viewport. `Performance`'s two regions clamp the same way against a `MinPerfW` floor. See
`EditorLayout.cs`'s own comments for the exact chain.

## 3. Tests

`Centauri.Tests/UI/EditorLayoutTests.cs` — pure geometry, no ImGui/GL, reachable via `internal` +
`InternalsVisibleTo`. Runs across a `[Theory]` matrix of 9 resolutions (640×480 up to 3840×2160,
including a portrait-ish 900×1440), 2 work-area origins (simulating e.g. a future global menu bar
shifting the viewport down), and 4 UI scales (`Widgets.FontScale` varies with configured font
size) — 72 combinations per test:

- **`Edit_TilesTheWorkAreaExactly_NoGapsNoOverlaps`** — every region is within the work area;
  every shared edge between neighbours (`TopBar.Bottom == LeftTools.Top`, `LeftTools.Right ==
  Viewport.Left`, etc.) matches exactly; every region reaches the far edges of the work area (no
  dead strip left uncovered); and — the one that actually catches an overlap *or* a gap that the
  edge checks alone might miss (e.g. a region shifted the same amount on both sides) — the five
  regions' areas sum to exactly the total work area.
- **`AllWorkspaces_NeverProduceNegativeSizes`** — across all three workspaces, every region's
  width/height is ≥ 0 at every resolution in the matrix, including the deliberately pathological
  640×480 and 900×1440 cases.
- **`Performance_StatsAndGraphsTileTheBodyExactly`** / **`Viewing_IsJustTheTopBarAndAFullWidthViewport`**
  — the other two workspaces' specific shapes.
- **`Edit_AtAPathologicallySmallResolution_StillFitsWithoutNegativeViewport`** — 64×64, smaller
  than the tool column + sidebar's combined design width, still produces non-negative sizes.

201 tests total in the suite after this (148 new). The actual ImGui rendering (window flags,
`SetNextWindowPos`/`Size`, docked-vs-floating flag differences) isn't unit-tested — it needs a live
ImGui frame — but was verified visually headless at four real resolutions (1024×768, 1280×720,
1920×1080, 2560×1440; see the screenshots in §1) plus all three workspaces, confirming the
math the tests pin actually matches what gets drawn on screen.

### A note on headless resolution testing

Getting real (not just synthetic) resolutions to screenshot took an extra step: this repo's window
config uses `WindowState.Maximized`, but **Xvfb has no window manager**, so GLFW's maximize hint is
a no-op under it — the window silently stays at Silk.NET's default `1280×720` regardless of the
Xvfb screen size. The screenshots above were captured with a temporary (uncommitted) explicit
`WindowOptions.Size` override to force real resolution variation for verification; the automated
`EditorLayoutTests` matrix above is the actual regression protection for resolution independence,
since it doesn't depend on any of that.

## 4. Panel docking mechanics

`PanelHost` (in `UI/Layout/`) is the shared plumbing every docked panel uses:

- `PanelHost.Place(rect, bgAlpha)` calls `SetNextWindowPos`/`SetNextWindowSize` from a `LayoutRect`.
- `PanelHost.DockedFlags` — `NoMove | NoResize | NoCollapse | NoSavedSettings |
  NoBringToFrontOnFocus`. Critically **not** `AlwaysAutoResize` (the flag every one of these panels
  used when they floated) — that flag overrides `SetNextWindowSize` and would silently break the
  tiling the moment any panel's content changed size.

`StatsOverlay`, `PerformancePanel`, `HierarchyPanel` (Outliner), and `PropertiesPanel` all take a
`LayoutRect` parameter now instead of positioning themselves; `TopBar` and `GizmoModeBar` do the
same. None of them know their own screen position — `UISystem.Render` is the one place that calls
`EditorLayout.Compute` and hands each panel the slot it got.
