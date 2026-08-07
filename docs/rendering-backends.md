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
The existing OpenGL draw path still consumes the compatibility IDs while also
binding the descriptor table, so this migration does not change current output.

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

`Auto` currently resolves to OpenGL. This is intentional: existing scene passes
still issue legacy OpenGL commands directly. Selecting Vulkan creates a real
Vulkan device and swapchain, but the game loop stops with a diagnostic before
loading legacy scene content. This prevents an apparently working but blank
Editor/Player window.

The remaining PMX work is to replace its OpenGL VAO/shader draw passes with a
backend-neutral pipeline and bind the new buffers/uniforms through Vulkan
resource sets. The other remaining passes are shadow, water, particle,
post-process, and ImGui. Once those passes no longer use `GraphicsDevice.Gl`, the
Auto branch can prefer Vulkan on Windows/Linux and retain OpenGL on macOS or as
fallback.
