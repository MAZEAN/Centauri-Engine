# Automated Tests (`Centauri.Tests`)

A real xunit project — `Centauri.Tests/Centauri.Tests.csproj`, part of `Centauri-Engine.sln`.
Formalizes the throwaway standalone-console-project pattern this repo used ad hoc before (see
CLAUDE.md's own note on it) into something that runs on every change instead of only when someone
remembers to write a scratch harness. No CI wiring yet — see §5.

## 1. Run the tests

```bash
dotnet test Centauri.Tests/Centauri.Tests.csproj -c Release
```

or, from the solution root, to build + test everything in one shot:

```bash
dotnet test Centauri-Engine.sln -c Release
```

Filter to one class or method (standard `dotnet test --filter` syntax):

```bash
dotnet test Centauri.Tests/Centauri.Tests.csproj --filter "FullyQualifiedName~CascadeBuilderTests"
dotnet test Centauri.Tests/Centauri.Tests.csproj --filter "FullyQualifiedName~Wire_FirstMatchWinsOnDuplicateParentNames"
```

No GL context, no window, no Xvfb needed for anything currently in this project — every test here
targets pure-CPU logic. Runs in well under a second.

## 2. What's covered so far

| Suite | Targets | What it actually checks |
|---|---|---|
| `Shadows/CascadeBuilderTests.cs` | `Rendering/Shadows/CascadeBuilder.cs` | Cascade-count clamping to `[1, MaxCascades]`, strictly-increasing split depths, the `reuse`-array optimization (same/different instance as appropriate), **determinism** for identical inputs (bit-identical `Matrix`/`Radius`/`DepthRange`), no non-finite output. |
| `Loading/EntityHierarchyWiringTests.cs` | `Loading/EntitySets/EntityHierarchyWiring.cs` | Parent resolution by name, a null/empty `Parent` field being a no-op, an unresolvable parent name not throwing, first-match-wins on duplicate names, and declaration-order independence (a child listed before its own parent in the source array still resolves — this is *why* `Wire` runs as a second pass after every entity in a file already exists). |

Both targets were picked because they're exactly the kind of logic CLAUDE.md's standalone-harness
note calls out: pure C#/`System.Numerics`, no GL, no window — `CascadeBuilder` takes a `Camera`
and `AppConfig` (both plain objects, no GL-backed state) and returns a `Cascade[]`;
`EntityHierarchyWiring.Wire` takes `(EntityDefinition, Entity)` pairs and links `Transform.Parent`
references, and `new Entity()`/`new Transform()` need no GL context either.

## 3. Project structure and conventions

- **One test file per source file**, same relative path under `Centauri.Tests/` as the source has
  under `Centauri/` (`Rendering/Shadows/CascadeBuilder.cs` → `Centauri.Tests/Shadows/
  CascadeBuilderTests.cs` — the `Rendering/` prefix is dropped since `Centauri.Tests`'s own root
  already disambiguates it from the source tree). Makes "is there a test file for this" a
  find-by-name question.
- **`ProjectReference`, not a rebuilt-`.dll`-plus-`HintPath`** (`Centauri.Tests.csproj` references
  `../Centauri/Centauri.csproj` directly) — this is the one place the standalone-harness pattern
  in CLAUDE.md is intentionally *not* followed: a throwaway harness references the already-built
  `Centauri.dll` because it's meant to be deleted after one use, but a permanent test project
  should always test against a fresh build, and `dotnet test`/`dotnet build` already handle
  building the referenced project first.
- **`InternalsVisibleTo`** (`Centauri.csproj`) grants `Centauri.Tests` access to `internal` types —
  `EntityHierarchyWiring`, `TrackedEntitySet`, `MaterialRegistry`, `ModelRegistry`,
  `ShadowCasterRenderer` and friends are `internal` on purpose (implementation details of their
  owning subsystem, not part of the engine's public surface), and this lets them stay that way
  instead of forcing a choice between "make it public just to test it" and "don't test it."
- **Naming**: `MethodUnderTest_Scenario_ExpectedBehavior` (e.g.
  `Build_ReusesArrayInstanceWhenLengthMatches`, `Wire_IgnoresUnresolvableParentNameWithoutThrowing`)
  — the test name alone should tell you what broke from a red run without opening the file.
- **Comments explain *why* a case is worth testing**, not what the assertion does — same house
  style as the rest of the codebase (see CLAUDE.md's commenting guidance). A test with no comment
  is one whose scenario is self-evident from its name.

## 4. Adding a new test

1. **Pick a target that's pure-CPU logic** — no `GL`/`GLShader`/`GLTexture`/render-target
   parameters, nothing that needs a window or a GL context to construct. Good signs: the class
   only touches `System.Numerics`, plain data (`AppConfig`, `EntityDefinition`), or other
   already-pure classes. `Camera`, `Transform`, `Entity` (constructed with `model: null`),
   `BoundingBox`, and any `Config/Settings/*.cs` class are all safe to instantiate directly in a
   test — none of them touch GL.
2. **If the target is `internal`**, that's fine — `InternalsVisibleTo` already covers it. Don't
   make something `public` just to reach it from a test.
3. **Write the smallest real object graph that exercises the behavior** — see
   `CascadeBuilderTests.MakeCamera`/`SceneBounds` or `EntityHierarchyWiringTests.Node` for the
   pattern: a small private helper that builds just enough of the surrounding graph, reused across
   the file's `[Fact]`/`[Theory]` methods.
4. **Verify the test can actually fail** before trusting it — temporarily break the logic it
   covers (comment out the line under test, flip a condition), confirm the expected test(s) go
   red, then revert. This project's own two suites were built this way (see their commit message
   for exactly what was broken and which tests caught it) — a test that's never been seen to fail
   is unverified, not passing.
5. **Run the full suite, not just the new test**, before considering it done —
   `dotnet test Centauri.Tests/Centauri.Tests.csproj -c Release`.

### What doesn't fit here (yet)

Anything that needs an actual GL context (shader compilation, texture decode + upload, a full
render) isn't covered by this project and shouldn't be forced into it — `Centauri.Tests` has no
window/Xvfb setup. For that, the existing headless pattern still applies: `CENTAURI_HEADLESS_FRAMES`
+ `CENTAURI_SCREENSHOT_PATH` + `xvfb-run` (see CLAUDE.md's "Headless / CI rendering" section) run
the real engine and produce a screenshot to inspect. That path is slow (seconds per run, real GL
driver overhead even under llvmpipe) and currently manual/ad hoc per change — formalizing *that*
into something `dotnet test` can drive is future work, not started here (see §5).

Material `extends` inheritance (`MaterialRegistry.ResolveJson`) and UV/path resolution
(`ApplyTexturePathPrefix`) are both still pure-CPU and GL-free, just not covered yet — good next
additions to this project rather than the headless path, since they need real temp `.mat` files
on disk but no GL context at all (`Directory.CreateTempSubdirectory()` + `File.WriteAllText` in a
test fixture, cleaned up in `Dispose`).

## 5. Known limitations / next steps

- **No CI.** `.github/` doesn't exist — nothing runs this project automatically on push yet.
  Deliberately not bundled with the tests themselves; see `Docs/Roadmaps/ENGINE_ROADMAP.md` Phase
  0 for why (a repo/hosting decision, not just code).
- **Coverage is a seed, not a target.** Two suites exist because they were the two most
  self-contained, highest-value pure-logic pieces at hand when this project was created — not
  because everything else is already covered. `ENGINE_ROADMAP.md` Phase 0 lists what's next
  (material `extends` merge, UV/path resolution).
- **No render-output testing.** See §4's "what doesn't fit here" — this is a real gap (the
  renderer is the largest, most actively-changing part of the engine, per `ENGINE_ROADMAP.md`'s
  own line-count survey), just not one this project's current shape is meant to close.
