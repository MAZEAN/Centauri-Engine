# Centauri Engine

A C# real-time rendering engine built directly on OpenGL 3.3 core via Silk.NET bindings (no external
game-engine framework). Forward+prepass renderer with CSM/PCSS shadows, SSR, GTAO, planar reflections,
TAA, and a procedural sky/IBL pipeline.

## Build & run

```bash
dotnet build Centauri-Engine.sln -c Release   # or -c Debug
dotnet run --project Centauri/Centauri.csproj -c Release
```

Target framework: `net10.0`. Config is loaded from `Centauri/Config/config.json` at startup
(`AppConfig`/`ConfigLoader`).

### Headless / CI rendering

The engine can run without a real display via Xvfb + Mesa llvmpipe (software rasterizer):

```bash
CENTAURI_HEADLESS_FRAMES=<n> CENTAURI_SCREENSHOT_PATH=<path.png> \
  xvfb-run -a dotnet run --project Centauri/Centauri.csproj -c Release
```

See `Centauri/Utils/Misc/HeadlessCapture.cs`. Useful for verifying a change doesn't crash or visibly
regress, but llvmpipe frame times (often 1–2s/frame) are **not** representative of real GPU
performance — don't draw fill-rate/timing conclusions from it, only correctness/visual ones.

A scene is two file types (see "Scene loading" below): the environment (`render.environmentPath`,
required — camera + skybox) and zero or more entity sets (`render.entitySetPaths`, empty by default).
The demo content lives at `Centauri/Loading/EntitySets/Demo.json` but isn't loaded unless listed in
`entitySetPaths`, and references model/texture assets that may not exist in every checkout (e.g. this
sandbox only ships `TestCube`/`TestPlane`/`Tree` under `Assets/Objects` and `Testing/Trees`). For
headless smoke-testing without the full asset pack, write a throwaway environment/entity-set JSON
using only the available models/no skybox panorama, and point `render.environmentPath`/
`entitySetPaths` at them temporarily — don't commit that swap.

## Scene loading

A scene is split into two independent file types, loaded by two separate classes:
- **Environment** (`Loading/Environment/`, `EnvironmentDefinition`/`EnvironmentLoader`) — camera(s)
  + skybox + an optional `"sun"` (reuses `EntityDefinition`'s schema wholesale). Exactly one,
  required, path set by `render.environmentPath`. Procedural sky/clouds/IBL and the Day/Night
  inspector section all require an actual `DirectionalLight` entity to read a sun direction from
  (`RenderingSystem.UpdateProceduralIbl`), so with entity sets empty by default the environment's
  `"sun"` is what makes those work out of the box — don't also put a directional light in an entity
  set unless you intend two suns (`DirectionalLights[0]` wins, order-dependent).
- **Entity sets** (`Loading/EntitySets/`, `EntitySetDefinition`/`EntitySetLoader`) — a list of
  entities. Zero or more, layered onto the environment at startup per `render.entitySetPaths`
  (default empty — a fresh project boots into an empty scene). Add entities live via the Outliner's
  "+ Add" row (composes from `ResourceSystem.ModelIds`), or Delete-key a selected one in Edit mode.
  Ctrl+S (`EntitySetLoader.Save()`) writes every tracked entity back to the file it came from — an
  entity created at runtime and never loaded from a file is attributed to
  `render.defaultEntitySetPath` until saved once. Only what the inspector can actually edit
  round-trips (name, enabled, transform, light); material *property* edits (Color/Roughness/
  Metallic/Translucency) aren't persisted — the schema only supports material *bindings* (paths),
  not inline scalar overrides. Camera/skybox edits aren't persisted either (`EnvironmentLoader` has
  no `Save()`).

## Project layout

```
Centauri/
  Engine.cs              Silk.NET IView lifecycle (OnLoad/OnUpdate/OnRender/OnClose), wires every system
  Program.cs              entry point
  Config/                 AppConfig + one Settings/*.cs per subsystem, loaded from Config/config.json
  Rendering/
    RenderingSystem.cs     top-level per-frame orchestration (Frame Tracy zone lives here)
    MainRenderer.cs         draws Scene meshes (instanced, triangle lists)
    Shadows/                CascadeBuilder, ShadowMapper, ShadowCache (CSM + PCSS)
    Prepass/, GTAO/, Reflections/(SSR, Planar), TAA/, IBL/, Postprocessing/, Renderers/(sky, clouds, grid)
    Culling/, Profiling/    Tracy (CPU) + GPUProfiler (GL_TIME_ELAPSED, double/triple-buffered) zones
  Graphics/
    Geometry/Model.cs       Assimp import (Model.Decode) -> Mesh; applies node transforms
    Resources/              GLShader (+ hot-reload support), GLTexture, HDR/EXR loader
  UI/                       ImGui-based editor UI (UISystem owns StatsOverlay, Outliner, Properties, Toolbar)
  World/                    Scene, Entity, Transform, Camera, Light, ECS-ish component collections
  Loading/                  Environment/ + EntitySets/ loaders (see "Scene loading" below), ComponentFactory
  Utils/                    Misc (Time, FrameStats, ShaderHotReload, HeadlessCapture), Math, Geometry, Caching
  Shaders/                  .vert/.frag/.comp GLSL sources
  Assets/, Testing/          content (models, textures, HDRIs) — CopyToOutputDirectory
Docs/                       COMMANDS.md, TODO.md, Documentation/ (Tracy profiler, sapling tree gen notes)
```

## Conventions worth knowing before editing

- **Tracy CPU zones**: `using var _ = Tracy.Scope("Name");` (or qualified `Profiling.Tracy.Scope(...)`
  if the file doesn't have `using Rendering.Profiling;`/`using Profiling;` in scope). Nest zones for
  sub-costs; the Tracy Statistics panel's "Self only" mode shows a zone's own time excluding children,
  not cumulative — read captures accordingly.
- **GPU timing**: `GPUProfiler.Measure("Name")` wraps `GL_TIME_ELAPSED` queries, double/triple-buffered
  so reads never stall the CPU.
- **Assimp matrix convention**: `Silk.NET.Assimp.Node.MTransformation` is typed as
  `System.Numerics.Matrix4x4` but is a raw memory overlay of Assimp's row-major/column-vector layout
  (translation in M14/M24/M34) — always `Matrix4x4.Transpose()` it before use; see `Model.cs`.
- **GLShader hot-reload** (`Debug.ShaderHotReload`, opt-in): `GLShader.TryReload()` compiles into a new
  program and only swaps `Handle` on success, so every existing holder of the shader object picks up
  the change automatically; a failed reload leaves the previous, working program untouched.
  `FileSystemWatcher` events fire off the GL thread — changed paths are buffered and applied from
  `ShaderHotReload.Poll()`, called once/frame from the GL thread.
- **Render-resolution scale** (`Render.RenderScale`): the scene renders at a fraction of window
  resolution and is upscaled for free by the existing linear-filtered tonemap sampling; only the final
  tonemap viewport stays native-resolution.
- **ImGui draw-list cost**: immediate-mode UI resubmits its full draw list every frame — there's no
  cross-frame caching. When a panel plots history (`UI/Panels/Graphs/*`), keep the per-frame primitive
  count (`AddLine`/`AddQuadFilled` calls, etc.) proportional to what's actually visible; prefer a single
  `AddPolyline`/low-level `PrimReserve`+`PrimWriteVtx`/`PrimWriteIdx` triangle-strip submission over one
  draw call per sample. Note `AddConvexPolyFilled` requires an actually convex outline — a stacked-area
  band with a wiggling top edge usually isn't, and feeding it a concave polygon silently corrupts the
  fill (overlapping/torn geometry) rather than erroring.
- **Standalone C# test harness pattern**: for pure-CPU logic that doesn't need a GL context (e.g.
  `CascadeBuilder` math, `Model.Decode()`), a throwaway console project referencing the built
  `Centauri.dll` (+ native deps like `libassimp.so.5` alongside it) via `<Reference><HintPath>` is
  faster than wiring a real test project.

## Delivery workflow this repo follows in Claude Code sessions

Changes are typically committed locally, rebased onto the latest `origin/main`, and verified in a
fresh `git worktree` (`git am` + `dotnet build`) before being handed off — direct pushes only happen
when explicitly requested. See git log for the granularity/style of commit messages expected (present
tense, explain *why* not *what*, flag any incidental/unscoped fixes separately in the message body).
