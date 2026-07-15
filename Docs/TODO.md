# TODO

> Project task tracker and roadmap (unordered)

---

## Features

* [x] Entity inspector / modifier
* [x] Skybox
* [x] IBL
* [x] Shadow Maps
* [x] Cascaded shadow maps (CSM)
* [x] Day/Night cycle
* [x] Screen Space Ambient Occlusion (SSAO)
* [x] Temporal Anti-Aliasing (TAA)
* [x] Screen Space Reflections (SSR) (with ray tracing or PBR Accumulation)
* [x] Reflection Probes (maybe "Planar Reflections" for mirrors)
* [x] GTAO (Ground-Truth AO)
* [x] Contact-hardening / PCSS
* [ ] Raytracing with BVH (only experimental, not real-time)
* [x] Physics engine integration (BEPUphysics2) + fixed timestep (foundation — see Docs/Documentation/PhysicsEngine.md; editor UI/serialization/kinematics still open)
* [ ] Terrain (technique unclear, maybe https://github.com/xandergos/terrain-diffusion/tree/master)
* [ ] Water simulation (Sea of Thieves algorithm)
* [x] Complex foliage rendering
* [ ] Displacement mapping (Parallax Occlusion Mapping (POM)) + self-shadowing
* [ ] Audio
* [ ] Skeletal animation / skinning

## Enhancements

* [x] Performance graphs
* [x] Light editing
* [x] bloom 
* [x] Instancing
* [x] Async asset loading
* [x] Sub-mesh materials
* [x] Procedural sky
* [x] Improved sky transitions
* [ ] (auto) LOD system (Impostor)
* [x] auto-exposure 
* [x] Triplanar / world-space UV projection
* [ ] volumetric fog
* [x] Tracy Profiler integration
* [x] Better wind
* [ ] Extend shadows to spot- and point lights
* [ ] Sky variantions
* [ ] Keybindings
* [x] UI improvements (DPI-scaling etc.)
* [x] Improve tree models
* [ ] Automated tests
* [x] Scene save/serialization
* [ ] Transform hierarchy in scene format
* [ ] Local-light shadows
* [ ] Better clouds
* [ ] wind (hierarchy) & leaves (orientation)
* [ ] GL 3.3 → 4.3 upgrade (enables clustered lighting, GPU particles & cheaper foliage)

## Optional
* [ ] Hosek-Wilkie sky algorithm
* [ ] Full raymarched volumetric clouds
* [x] Replace MSAA

---

## Bug Fixes

* [ ] Leaves become disconnected from the branch (not linked)

---

## Notes

* Reflections: The current design switches rather than combines.
A more advanced setup uses planar as the floor's base and lets SSR add perspective-accurate contact reflections
on top (mix(planar, ssr, ssrConfidence)) — better for objects standing in water.
Worth doing when you add real water, alongside the distortion wave normals.
