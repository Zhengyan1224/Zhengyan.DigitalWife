# Android Rendering Parity

This document is the working parity matrix for `AndroidVulkanGame`,
`AndroidPmxSceneRenderer` (OpenGL ES), and the desktop GamePlayer passes.

## Pass Matrix

| Feature | PC | Android Vulkan | Android OpenGL ES | Current status |
| --- | --- | --- | --- | --- |
| PMX main material | Shared PMX pass, diffuse/sphere/toon, alpha modes, local lights | Shared Vulkan PMX pass | GLES PMX shader | Aligned for the fixed material contract |
| PMX skinning | CPU/OpenGL/Vulkan compute paths | CPU or Vulkan compute | CPU or GLES uniform BDEF path, CPU fallback for SDEF/QDEF/morphs | Semantic parity; performance differs |
| PMX edge | Auxiliary edge pass | Vulkan auxiliary pass | GLES edge pass | Aligned |
| Directional shadow | PCF directional map | Shared shadow renderer | RGBA-packed depth map, configurable quality size, 3x3 PCF | Aligned in behavior; storage format differs |
| Point-light shadow | Local-light atlas, up to 2 shadowed point lights | Same atlas path | Two cube shadow maps, up to 2 shadowed point lights | Semantic parity for the supported budget |
| Spot-light shadow | Local-light atlas, up to 4 shadowed spot lights | Same atlas path | Two independent spot maps, up to 2 shadowed spot lights | Known GLES budget difference |
| Ground shadow | PMX GroundShadow auxiliary pass | Shared auxiliary pass | GLES ground-shadow pass when directional map is unavailable | Aligned |
| Skybox | Inverse view-projection equirectangular sampling | Fullscreen direction pass | Fullscreen direction pass, clamped vertical wrap | Aligned |
| Water surface | Gerstner mesh, animated normals, sky/planar reflection, ripples | `VeldridWaterRenderer` | GLES water pass with the same 48-ripple contract | Aligned; texture/FBO implementation differs |
| Underwater post process | Color + depth capture, fog, absorption, caustics, bubbles, distortion | Vulkan post pass | GLES color + depth FBO and post shader | Aligned |
| Particle simulation | CPU particle component, per-camera billboard, collision and water interaction | Shared particle component | Deterministic GLES simulation, per-camera geometry rebuild, collision and water interaction | Aligned behavior; implementation differs |
| Particle shadow | Directional/local light shadow passes | Shared auxiliary passes | GLES particle shadow pass | Aligned |
| Textured plane | Texture, billboard, shadow receive, mirror reflection | Vulkan plane pass | GLES plane pass, dynamic transform/size/tint | Aligned for fixed texture path |
| Planar reflection | Per-surface target, one-level recursion | Vulkan render target | GLES FBO target, one-level recursion | Aligned |
| RenderTexture | Every-frame/fixed-rate/on-demand | Shared render-target manager | GLES FBO manager with normalized refresh modes | Aligned |
| Camera viewports | Local clear, camera aspect, overlay layout | Vulkan viewport loop | GLES viewport loop with local sprite layout | Aligned |
| Runtime debug lines | Line renderer | Vulkan line renderer | GLES line renderer | Aligned |
| Screen sprites | Backend screen-sprite renderer | Vulkan sprite renderer | GLES overlay quad renderer | Aligned for scene sprites |

## Backend-Specific Differences

These are intentional or resource-budget differences rather than missing
scene semantics:

1. GLES uses CPU skinning when the model cannot fit the 96-bone uniform path.
   Vulkan may use compute skinning when enabled and available.
2. GLES stores directional/spot shadow depth in RGBA8 and samples it manually;
   Vulkan samples a depth attachment or atlas view.
3. GLES uses two cube maps for point shadows and two 2D maps for spot shadows.
   The current Android quality budget therefore exposes fewer spot shadow slots
   than the desktop/Vulkan atlas (2 instead of 4).
4. GLES uses explicit GL state restoration after every auxiliary pass. Vulkan
   encodes the equivalent state in pipelines.

## Remaining Functional Gaps

These items are not silently treated as parity-complete:

1. Android GLES does not yet run the desktop custom PMX/plane shader override
   API. The Vulkan path accepts the portable SPIR-V contract; GLES still uses
   the built-in Android shader programs.
2. Runtime PMX material texture overrides and custom shader uniforms are fully
   wired through the Vulkan component path but are not exposed by the GLES
   `PmxGpuModel` wrapper.
3. GLES currently keeps two spot shadow maps. Moving to four would require two
   additional FBOs, sampler units, vertex shadow matrices and fragment branches;
   this should be implemented together with a measured texture-unit fallback.
4. Full Android APK/device validation still requires JDK 21. The current host
   has JDK 26, so the static C# compile is the available verification step.

## Regression Checklist

- Skybox top/bottom orientation matches Vulkan.
- Water reflection is not vertically inverted and remains visible when sky
  reflection strength is zero but mirror reflection is enabled.
- Underwater capture contains both color and depth before the post pass.
- Particle counts remain stable until each particle's own lifetime expires.
- Rain, Sakura and velocity-aligned particles are visible in both backends.
- A second camera viewport receives its own sprite layout and particle
  billboard orientation.
- Runtime debug lines, ground shadows and disabled entities do not leak into
  reflection or RenderTexture passes.
