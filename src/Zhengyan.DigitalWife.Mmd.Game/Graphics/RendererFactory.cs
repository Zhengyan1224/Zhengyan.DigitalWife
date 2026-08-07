using Silk.NET.Windowing;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

public static class RendererFactory
{
    private static readonly IRendererFactory OpenGl = new OpenGlRendererFactory();
    private static readonly IRendererFactory Vulkan = new VulkanRendererFactory();

    public static RendererSelection Select(GraphicsBackend requestedBackend)
    {
        if (requestedBackend == GraphicsBackend.OpenGL)
        {
            return new RendererSelection(requestedBackend, GraphicsBackend.OpenGL);
        }

        if (requestedBackend == GraphicsBackend.Vulkan)
        {
            if (!Vulkan.IsSupported(out string reason))
            {
                throw new NotSupportedException($"Vulkan was explicitly selected but is unavailable: {reason}");
            }

            return new RendererSelection(requestedBackend, GraphicsBackend.Vulkan);
        }

        // Auto remains conservative until every scene pass has a backend-neutral implementation.
        // This prevents an existing project from opening as a blank Vulkan swapchain.
        return new RendererSelection(requestedBackend, GraphicsBackend.OpenGL,
            "Vulkan is available only for the staged device/swapchain path; scene passes still require OpenGL.");
    }

    public static IRenderer Create(RendererSelection selection) => selection.ResolvedBackend switch
    {
        GraphicsBackend.OpenGL => OpenGl.Create(),
        GraphicsBackend.Vulkan => Vulkan.Create(),
        _ => throw new NotSupportedException($"No renderer is registered for {selection.ResolvedBackend}.")
    };

    public static void ConfigureWindow(ref WindowOptions options, GraphicsBackend backend)
    {
        options.API = backend switch
        {
            GraphicsBackend.OpenGL => new GraphicsAPI(ContextAPI.OpenGLES, new APIVersion(3, 0)),
            GraphicsBackend.Vulkan => GraphicsAPI.None,
            _ => throw new ArgumentOutOfRangeException(nameof(backend), backend, null)
        };
    }

    private sealed class OpenGlRendererFactory : IRendererFactory
    {
        public GraphicsBackend Backend => GraphicsBackend.OpenGL;

        public bool IsSupported(out string reason)
        {
            reason = string.Empty;
            return true;
        }

        public IRenderer Create() => new OpenGlRenderer();
    }

    private sealed class VulkanRendererFactory : IRendererFactory
    {
        public GraphicsBackend Backend => GraphicsBackend.Vulkan;

        public bool IsSupported(out string reason) => VulkanRenderer.IsSupported(out reason);

        public IRenderer Create() => new VulkanRenderer();
    }
}
