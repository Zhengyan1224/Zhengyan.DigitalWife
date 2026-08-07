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
sampling, and optional reflection resources. Arbitrary user-provided custom
GLSL remains an OpenGL compatibility feature, while `PortableShaderContract`
validates the explicit GLSL 450 resource contract intended for Vulkan and
future backends.

`VeldridScreenSpriteRenderer` now provides that Vulkan implementation: it owns a
dynamic vertex buffer, uniform buffer, sampler, descriptor layouts, SPIR-V
shaders, alpha-blended pipeline, and per-texture resource-set cache. Renderer
creation is selected through `IScreenSpriteRenderer`, so Editor and Player no
longer instantiate the OpenGL sprite renderer directly.

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

The GameEditor setting is also written to `GameEditor.settings.json` beside the
editor executable. This is needed because a window's graphics API is selected
before the project preview window is created. Changing the setting takes effect
on the next GameEditor start. GamePlayer reads the project setting before it
creates its window; `--graphics-backend` can override it for diagnostics.

## Auto policy

`Auto` currently resolves to OpenGL. This remains intentional while existing
scene passes still issue legacy OpenGL commands directly. Selecting Vulkan now
supports PMX main/auxiliary passes, screen sprites, and the fixed textured-plane
pass; remaining legacy scene passes still produce a backend diagnostic until
they are migrated.

The portable custom-shader contract requires `#version 450`, explicit vertex
attribute/varying locations, a `PlaneFrame` block at `set=0,binding=0`, and
sampled texture/sampler resources at bindings 1 through 6. Call
`PortableShaderContract.ValidatePlane(...)` (or the component helper) to get
actionable diagnostics before loading a shader. The contract deliberately
avoids implicit uniform locations so a future Direct3D backend can consume the
same reflected resources. The plane resource map is: binding 1 base texture,
2 base sampler, 3 shadow texture, 4 shadow sampler, 5 reflection texture, and
6 reflection sampler. Legacy `#version 300 es` shaders continue to work only
through the OpenGL compatibility API.

Remaining scene passes are water, particle, post-process, debug drawing, and
ImGui. Once those paths no longer use `GraphicsDevice.Gl`, the Auto branch can
prefer Vulkan on Windows/Linux and retain OpenGL on macOS or as fallback.
