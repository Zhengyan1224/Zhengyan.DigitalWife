# Rendering Backends

The engine now routes window graphics through `IRenderer` and `RendererFactory`.
The game loop no longer creates an OpenGL context directly. `OpenGlRenderer` owns
the existing OpenGL ES context and compatibility bridge, while `VulkanRenderer`
owns a Veldrid Vulkan device, command list, and swapchain.

Off-screen resources now follow the same boundary through `IRenderTarget`.
OpenGL uses the existing FBO implementation and Vulkan uses `VeldridRenderTarget`
with real color/depth textures and a Veldrid framebuffer. Editor and Player
RenderTexture managers create targets through `GraphicsDevice`, so target
allocation no longer requires an OpenGL object in those managers.

Sampled textures now have the parallel `ITexture2D` contract. The OpenGL
implementation preserves the existing DDS/BMP/image loading behavior, while
`VeldridTexture2D` uploads RGBA image data into a Vulkan sampled texture. The
factory is exposed by `GraphicsDevice.CreateTexture2D()` so individual scene
components can migrate without changing the renderer selection code.
The shared decoder now feeds DDS, bitfield BMP, and stb_image formats into both
backends.

Screen-sprite draw commands now carry `RuntimeTextureHandle` values instead of
bare OpenGL IDs. The current OpenGL sprite renderer consumes the compatibility
ID; a Vulkan sprite renderer can consume the native resource carried by the same
command without changing Editor or Player layout code.

PMX model loading now owns its GPU data through `PmxGpuResources`. Position,
normal, UV, and index data use backend-neutral vertex/index buffers; frame and
material data use dynamic uniform buffers. PMX material textures are exposed as
three-binding descriptor tables (base, sphere, toon) with backend samplers, and
embedded toon textures are created through `GraphicsDevice.CreateTexture2D()`.

The PMX main material pass now runs through `IPmxMainPassRenderer`. The OpenGL
implementation owns the legacy program/VAO and preserves shadow-map and material
override behavior. The Vulkan implementation owns SPIR-V shaders, frame/material
resource layouts, descriptor sets, culling variants, and indexed draw commands.
Pipelines are cached per framebuffer output description so the same pass can
target both the swapchain and Editor render textures.

The fixed PMX auxiliary passes now run through `IPmxAuxiliaryPassRenderer`.
OpenGL keeps the existing edge, projected ground-shadow, and shadow-depth
programs inside its backend implementation. Vulkan has corresponding SPIR-V
shaders, pass-specific uniform buffers and descriptor sets, framebuffer-aware
pipeline caches, material culling variants, alpha/stencil ground-shadow state,
and indexed draw commands. The PMX component no longer owns OpenGL shader or VAO
objects for these passes. Shadow-map allocation and depth-pass scheduling now
use `IShadowMapTarget`, with an OpenGL depth FBO and a Vulkan sampled depth
texture/framebuffer implementation. Scene-level restore callbacks now use
`IRenderTarget.ResumePass()` and `GraphicsDevice.RestoreBackBuffer()`, including
the Editor and Player shadow rendering callbacks. `TexturedPlaneComponent` has
a Vulkan fixed pass with backend-neutral base textures, manual depth shadow
sampling, and optional reflection resources. Custom components can provide a
GLSL vertex/fragment pair for OpenGL and a separately compiled SPIR-V
vertex/fragment pair for Vulkan. Vulkan loads the `.spv` modules directly and
does not compile the user GLSL path.

`VeldridScreenSpriteRenderer` now provides that Vulkan implementation: it owns a
dynamic vertex buffer, uniform buffer, sampler, descriptor layouts, SPIR-V
shaders, alpha-blended pipeline, and per-texture resource-set cache. Renderer
creation is selected through `IScreenSpriteRenderer`, so Editor and Player no
longer instantiate the OpenGL sprite renderer directly.

The remaining scene-level passes now have Vulkan implementations as well:
`VeldridSkyboxRenderer`, `VeldridWaterRenderer`, `VeldridParticleRenderer`,
`VeldridUnderwaterPostProcessRenderer`, and `VeldridLineRenderer`. Water
reflection targets are allocated through `IRenderTarget` and return native
texture handles, so reflection rendering no longer binds an OpenGL FBO. The
Vulkan water pass also consumes the same 48-ripple state as OpenGL, and the
underwater post-process samples a linearized off-screen depth texture for fog
and absorption. Editor
and Player ImGui overlays use the engine-owned Veldrid ImGui renderer on Vulkan
and preserve the Silk controller on OpenGL; overlay images use backend-native
ImGui texture bindings.
The loading screen also has a Vulkan quad renderer. Desktop sprite click-through
uses the backend-neutral `IRenderer.TryReadBackBufferRgba` contract: OpenGL uses
`ReadPixels`, while Vulkan records the swapchain copy into a three-slot staging
ring. Fences allow a completed image from an earlier frame to be mapped without
calling `WaitForIdle()` every frame. The mapped image is normalized to the same
bottom-left RGBA layout before native window regions are updated. Windows,
macOS, and X11 have native window-region implementations. Native Wayland input
regions are not implemented yet; Linux desktop-sprite click-through currently
requires an X11 session or `GLFW_PLATFORM=x11`.

## Selecting a backend

Project files store the setting at `runtime.graphicsBackend`:

```json
{
  "runtime": {
    "graphicsBackend": "Auto"
  }
}
```

Accepted values are `Auto`, `OpenGL`, and `Vulkan` (case-insensitive; `GL` and
`VK` are accepted aliases by the parser).

PMX compute preferences are stored independently:

```json
{
  "runtime": {
    "graphicsBackend": "Vulkan",
    "useOpenCL": true,
    "useVulkanCompute": true
  }
}
```

The active graphics backend determines which preference is legal. OpenGL may
use OpenCL skinning when `useOpenCL` is enabled. Vulkan never probes or loads
OpenCL; it may use the Vulkan compute pipeline when `useVulkanCompute` is
enabled. Both GPU paths fall back to CPU skinning when initialization or a
dispatch fails. The Editor displays only the compute option applicable to the
selected graphics backend.

The GameEditor setting is also written to `GameEditor.settings.json` beside the
editor executable. This is needed because a window's graphics API is selected
before the project preview window is created. Changing the setting takes effect
on the next GameEditor start. GamePlayer reads the project setting before it
creates its window; `--graphics-backend` can override it for diagnostics.

## Auto policy

On Windows and Linux, `Auto` selects Vulkan when Veldrid finds a Vulkan loader
and compatible physical device, and otherwise falls back to OpenGL. On macOS it
selects OpenGL. Projects that contain legacy OpenGL-only custom components can
pin the setting to `OpenGL`. Vulkan supports PMX, skybox, water, particles,
screen sprites, textured planes, post-process, debug lines, reflections, loading
screens, and ImGui overlays.

Viewport rendering now uses `IRenderer.ClearViewport` for every camera on both
backends. Vulkan clears the selected color/depth/stencil rectangle through a
small utility pass, so overlapping camera viewports cannot inherit depth from a
previous camera. `IRenderTarget.ForceOpaqueAlpha` is also implemented by both
backends; the Editor uses it when compositing a preview whose clear alpha is not
opaque.

Backend-specific scene pass allocation is centralized in
`IRenderBackendServices`. Skybox, water, particle, loading-screen, line/debug,
textured-plane, underwater, ImGui, and shadow-map targets are requested from
that service rather than selected by scene code. OpenGL may still return a null
optional pass while its legacy compatibility implementation is active; a new
backend must provide these pass interfaces and does not need to be referenced
by the shared components.

The four-path custom shader API is:

```csharp
Entity.SetCustomShader(
    "assets/shaders/model.vert",
    "assets/shaders/model.frag",
    "assets/shaders/model.vert.spv",
    "assets/shaders/model.frag.spv");
```

The first pair is GLSL for OpenGL; the second pair is precompiled SPIR-V for
Vulkan. Every SPIR-V module must use entry point `main` and match the engine
pipeline layout. Plane shaders use vertex locations 0 (position) and 1 (UV),
with `PlaneFrame` at set 0 binding 0 and base/shadow/reflection texture-sampler
pairs at bindings 1 through 6. PMX shaders use vertex locations 0 (position),
1 (normal), and 2 (UV). PMX set 0 contains the frame block, shadow texture, and
shadow sampler at bindings 0 through 2. Set 1 contains the material block at
binding 0 and base/sphere/toon texture-sampler pairs at bindings 1 through 6.
`VulkanShaderContract.ValidatePair` runs before Vulkan pipeline creation. It
checks the SPIR-V magic/word alignment, cross-stage interface validity, and
rejects descriptor names outside the pass layout, producing an actionable
`InvalidDataException` instead of a deferred driver error. Descriptor member
names inside uniform blocks are still intentionally not inferred from SPIR-V;
custom named uniforms therefore remain an OpenGL-only setter contract until a
future explicit uniform metadata format is added. OpenGL remains the
compatibility fallback on macOS.

`GraphicsDevice.Gl` is retained only as a documented compatibility bridge for
the remaining OpenGL legacy paths. It is not touched by Vulkan execution and
should not be used by new rendering components.
