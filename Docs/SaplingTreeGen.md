# Sapling Tree Gen — Key Settings

Blender add-on: **Add > Curve > Sapling Tree Gen**

## Presets
Start from a built-in preset (Quaking Aspen, Black Tupelo, Palm, Bamboo, etc.) and adjust from there rather than starting from scratch.

## Geometry / Levels
- **Levels (1–4)** — trunk → branches → sub-branches → twigs. 3 for mid-distance trees, 4 for hero/close-up.
- **Length / Length Variation** (per level) — branch length + randomness.
- **Taper** — how much a branch narrows toward its tip.
- **Bevel + Resolution (resU)** — converts curve to renderable mesh. Low res while iterating, higher for final render.

## Branching / Splitting
- **Base Splits** — forks at trunk base (multi-trunk look).
- **Split Angle / Variation** — angle between forks.
- **Branches (per level)** — number of child branches per parent.
- **Down Angle / Variation** — droop angle of children off parent; big silhouette impact.
- **Rotate Angle / Variation** — angular spread around parent; avoids flat/planar branching.
- **Curve / CurveV / CurveBack** — bending along branch length for natural sway.

## Pruning
- **Prune Ratio** — cuts branches to fit an implied canopy envelope; shapes the crown.
- **Prune Width / Width Peak / Power** — controls envelope shape (round, conical, etc.).

## Leaves
- **Leaf Shape / Custom Object** — custom mesh for close-ups, billboard shapes for background trees.
- **Leaf Count / Size / Size Variation**
- **Leaf Random Orientation/Angle** — avoids uniform "combed" look.
- Disable leaves while tuning branch structure (viewport cost).

## Animation / Armature
- **Generate Armature** — adds bones per branch for wind/manual posing.
- **Wind Frequency / Amplitude** — quick procedural sway animation.

## Workflow Tips
- Reroll **Seed** for free variation without retuning — good for forest populating.
- Tune structure first at low bevel resolution → add leaves → raise resolution last.
- For real-time/engine use: prefer simple leaf billboards/cards over full leaf meshes to control poly count.

## Finalizing the Tree

Sapling generates **curve objects**, not meshes — they need conversion before export.

- **Convert to Mesh**: `Object > Convert > Mesh` (or Ctrl+C) on the branch curve. Do this only after you're happy with bevel/resolution settings — converting locks in geometry, further Sapling parameter tweaks won't propagate.
- **Separate objects**: Sapling typically generates branches and leaves as separate objects (and optionally an armature). Keep them separate for export if you want independent materials/LOD, or join (Ctrl+J) if you want a single mesh.
- **Clean up**:
    - Apply all transforms (`Ctrl+A > All Transforms`) before export.
    - Merge by distance (`M > By Distance`) to remove duplicate verts from bevel conversion.
    - Recalculate normals (`Shift+N`) — twisted branch curves can produce inverted normals.
- **UVs**: Sapling auto-generates basic UVs along branches; check unwrapping quality, especially at branch splits — may need manual seam adjustment for bark textures to avoid stretching.
- **LOD**: consider decimating distant-tree versions (Decimate modifier) since trunk/branch curves can get vert-heavy at high resolution/levels.
- **Armature**: if you generated one for wind animation, decide whether your engine pipeline actually needs bone-based wind, or if you'll do wind via vertex shader (common for real-time — cheaper, no skinning cost). If not needed, delete the armature and armature modifier before export.

## Export (for engine/real-time use, e.g. glTF/FBX pipelines)
- **Format**: prefer **glTF 2.0** for a modern engine pipeline (compact, PBR-friendly, well-supported by Silk.NET-adjacent tooling via Assimp or glTF loaders); FBX if your pipeline is already built around it.
- **Axis/orientation**: Blender is Z-up; OpenGL conventions are typically Y-up — set the exporter's up-axis conversion (glTF exporter does this automatically; FBX exporter needs `-Z Forward, Y Up` or equivalent set explicitly).
- **Scale**: apply scale before export (Blender units → engine units mismatch is a common source of oversized/tiny trees).
- **Materials**: bake or reference PBR maps (albedo/normal/roughness) consistent with your engine's material system before export, rather than relying on Blender-only shader nodes that won't translate.
- **Batching**: if exporting a forest, decide whether each tree instance is a unique mesh (varied via seed) or a shared mesh instanced many times — instancing is far cheaper at runtime.