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
* [ ] Physics engine integration (BEPUphysics2) + fixed timestep
* [ ] Terrain (technique unclear, maybe https://github.com/xandergos/terrain-diffusion/tree/master)
* [ ] Water simulation (Sea of Thieves algorithm)
* [x] Complex foliage rendering
* [ ] Displacement or Bump mapping
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
* [x] Better wind (hierarchy) & leaves (orientation)
* [ ] Extend shadows to spot- and point lights
* [ ] Sky variantions
* [ ] Keybindings
* [x] UI improvements (DPI-scaling etc.)
* [x] Improve tree models
* [ ] Automated tests
* [ ] Scene save/serialization
* [ ] Transform hierarchy in scene format
* [ ] Local-light shadows

## Optional
* [ ] Hosek-Wilkie sky algorithm
* [ ] Full raymarched volumetric clouds
* [ ] Replace MSAA

---

## Bug Fixes

* [ ] Leaves become disconnected from the branch (not linked)

---

## Notes

* Reflections: The current design switches rather than combines.
A more advanced setup uses planar as the floor's base and lets SSR add perspective-accurate contact reflections
on top (mix(planar, ssr, ssrConfidence)) — better for objects standing in water.
Worth doing when you add real water, alongside the distortion wave normals.
