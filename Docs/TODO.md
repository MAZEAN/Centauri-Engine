# TODO

> Project task tracker and roadmap.

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
* [ ] GTAO (Ground-Truth AO)
* [ ] Contact-hardening / PCSS
* [ ] Raytracing with BVH (only experimental, not real-time)
* [ ] Physics engine integration (BEPUphysics2)
* [ ] Basis for game
* [ ] Terrain
* [ ] Water simulation (Sea of Thieves algorithm)
* [ ] Complex foliage rendering

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
* [ ] volumetric fog
* [ ] Tracy Profiler integration
* [ ] Better wind (hierarchy) & leaves (orientation)
* [ ] Extend shadows to spot- and point lights
* [ ] Sky variantions
* [ ] Keybindings
* [ ] UI improvements (DPI-scaling etc.)

## Optional
* [ ] Hosek-Wilkie sky algorithm
* [ ] Full raymarched volumetric clouds

---

## Bug Fixes

---

## Notes

* Reflections: The current design switches rather than combines.
A more advanced setup uses planar as the floor's base and lets SSR add perspective-accurate contact reflections
on top (mix(planar, ssr, ssrConfidence)) — better for objects standing in water.
Worth doing when you add real water, alongside the distortion wave normals.
